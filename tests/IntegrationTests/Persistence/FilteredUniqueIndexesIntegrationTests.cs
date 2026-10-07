// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.UseCases.Admin.Persons.Commands;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Application.UseCases.Security.Auth.Register;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JOIN.IntegrationTests.Persistence;

/// <summary>
/// SPEC 40 (F3.2) — Runtime coverage of the filtered unique indexes (<c>WHERE GcRecord = 0</c>)
/// against a real SQL Server (Testcontainers in <see cref="CustomWebApplicationFactory"/>).
/// Soft-deleted rows sharing a natural key on the same day must no longer collide, while
/// uniqueness between active rows is preserved. The companion
/// <see cref="UniqueIndexSoftDeleteGuardTests"/> enforces the model contract.
/// </summary>
public sealed class FilteredUniqueIndexesIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    // Fixed deletion instant so every soft delete in a test gets the same yyyyMMdd GcRecord
    // stamp — the exact scenario that used to violate the old (…, GcRecord) unique keys.
    private static readonly DateTime SameDayUtc = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    private readonly CustomWebApplicationFactory _factory;

    public FilteredUniqueIndexesIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// D.1 — <c>PersonContact</c>: crear, borrar, recrear y volver a borrar el mismo contacto
    /// el mismo día no lanza excepción (antes: violación de índice único).
    /// </summary>
    [Fact]
    public async Task PersonContact_DeleteRecreateDeleteSameDay_DoesNotThrow()
    {
        var companyId = await SeedCompanyAsync();
        var personId = await SeedPersonAsync(companyId);
        const string email = "juan@x.com";

        await using var scope = CreateScopeAsCompany(companyId);

        var first = PersonContact.Create(companyId, personId, ContactType.PrimaryEmail, email);
        scope.Context.PersonContacts.Add(first);
        await scope.Context.SaveChangesAsync();

        first.MarkAsDeleted(SameDayUtc);
        await scope.Context.SaveChangesAsync();

        var second = PersonContact.Create(companyId, personId, ContactType.PrimaryEmail, email);
        scope.Context.PersonContacts.Add(second);
        await scope.Context.SaveChangesAsync();

        second.MarkAsDeleted(SameDayUtc);
        var act = async () => await scope.Context.SaveChangesAsync();

        await act.Should().NotThrowAsync();
        first.GcRecord.Should().Be(second.GcRecord, "both rows must share the same-day deletion stamp");
    }

    /// <summary>
    /// D.2 — <c>PUT /persons/{id}</c> tres veces en el mismo día quitando / agregando / quitando
    /// el mismo email: las tres devuelven 200 (antes la tercera devolvía 500).
    /// </summary>
    [Fact]
    public async Task UpdatePerson_RemoveAddRemoveSameEmailSameDay_AllReturn200()
    {
        var companyId = await SeedCompanyAsync();
        var identificationTypeId = await SeedIdentificationTypeAsync();
        var personId = await SeedPersonAsync(companyId, identificationTypeId);
        const string email = "juan@x.com";

        await using (var scope = CreateScopeAsCompany(companyId))
        {
            scope.Context.PersonContacts.Add(
                PersonContact.Create(companyId, personId, ContactType.PrimaryEmail, email));
            await scope.Context.SaveChangesAsync();
        }

        using var client = await CreateAuthenticatedClientAsync(companyId);

        UpdatePersonCommand BuildUpdate(params UpdatePersonCommand.UpdatePersonContactDto[] contacts) => new()
        {
            Id = personId,
            PersonType = PersonType.Legal,
            FirstName = "Juan",
            LastName = "Perez",
            IdentificationTypeId = identificationTypeId,
            IdentificationNumber = $"ID-{personId:N}".Substring(0, 20),
            Contacts = contacts
        };

        var emailContact = new UpdatePersonCommand.UpdatePersonContactDto
        {
            ContactType = nameof(ContactType.PrimaryEmail),
            ContactValue = email,
            IsPrimary = true
        };

        var removeResponse = await client.PutAsJsonAsync($"/api/v1/persons/{personId}", BuildUpdate());
        removeResponse.StatusCode.Should().Be(HttpStatusCode.OK, await removeResponse.Content.ReadAsStringAsync());

        var addResponse = await client.PutAsJsonAsync($"/api/v1/persons/{personId}", BuildUpdate(emailContact));
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK, await addResponse.Content.ReadAsStringAsync());

        var removeAgainResponse = await client.PutAsJsonAsync($"/api/v1/persons/{personId}", BuildUpdate());
        removeAgainResponse.StatusCode.Should().Be(HttpStatusCode.OK, await removeAgainResponse.Content.ReadAsStringAsync());

        await using var verifyScope = CreateScopeAsCompany(companyId);
        var rows = await verifyScope.Context.PersonContacts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.PersonId == personId && c.ContactValue == email)
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(c => c.GcRecord != BaseAuditableEntity.ActiveGcRecord);
        rows.Select(c => c.GcRecord).Distinct().Should().ContainSingle("both deletions happened the same day");
    }

    /// <summary>
    /// D.3 — <c>Gender</c>: dos géneros activos con el mismo nombre en la misma empresa siguen
    /// siendo rechazados (la unicidad entre activos se preserva).
    /// </summary>
    [Fact]
    public async Task Gender_TwoActiveWithSameNameInSameCompany_IsRejected()
    {
        var companyId = await SeedCompanyAsync();

        await using var scope = CreateScopeAsCompany(companyId);
        scope.Context.Genders.Add(Gender.Create(companyId, "G1", "Duplicado"));
        await scope.Context.SaveChangesAsync();

        scope.Context.Genders.Add(Gender.Create(companyId, "G2", "Duplicado"));
        var act = async () => await scope.Context.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<Exception>()
            .WithMessage("*UX_Genders_Company_Name*");
    }

    /// <summary>
    /// D.4 — <c>Gender</c>: el mismo nombre en empresas distintas está permitido.
    /// </summary>
    [Fact]
    public async Task Gender_SameNameInDifferentCompanies_IsAllowed()
    {
        var companyA = await SeedCompanyAsync();
        var companyB = await SeedCompanyAsync();

        await using (var scopeA = CreateScopeAsCompany(companyA))
        {
            scopeA.Context.Genders.Add(Gender.Create(companyA, "G1", "Compartido"));
            await scopeA.Context.SaveChangesAsync();
        }

        await using var scopeB = CreateScopeAsCompany(companyB);
        scopeB.Context.Genders.Add(Gender.Create(companyB, "G1", "Compartido"));
        var act = async () => await scopeB.Context.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// D.5 — <c>Region</c>: borrar y recrear con el mismo nombre/país dos veces el mismo día
    /// no lanza excepción.
    /// </summary>
    [Fact]
    public async Task Region_DeleteAndRecreateTwiceSameDay_DoesNotThrow()
    {
        var companyId = await SeedCompanyAsync();
        var countryId = await SeedCountryAsync();

        await using var scope = CreateScopeAsCompany(companyId);

        var first = NewRegion(companyId, countryId);
        scope.Context.Regions.Add(first);
        await scope.Context.SaveChangesAsync();

        first.MarkAsDeleted(SameDayUtc);
        await scope.Context.SaveChangesAsync();

        var second = NewRegion(companyId, countryId);
        scope.Context.Regions.Add(second);
        await scope.Context.SaveChangesAsync();

        second.MarkAsDeleted(SameDayUtc);
        await scope.Context.SaveChangesAsync();

        var third = NewRegion(companyId, countryId);
        scope.Context.Regions.Add(third);
        var act = async () => await scope.Context.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private static Region NewRegion(Guid companyId, Guid countryId) => new()
    {
        CompanyId = companyId,
        CountryId = countryId,
        Name = "Pacifico",
        Code = "PA",
        GcRecord = BaseAuditableEntity.ActiveGcRecord,
        CreatedBy = nameof(FilteredUniqueIndexesIntegrationTests)
    };

    private async Task<Guid> SeedCompanyAsync()
    {
        await using var seedScope = _factory.Services.CreateAsyncScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var company = new Company
        {
            Name = $"JOIN-U-{Guid.NewGuid():N}".Substring(0, 18),
            TaxId = $"RUCU{Guid.NewGuid():N}".Substring(0, 16),
            IsActive = true,
            GcRecord = BaseAuditableEntity.ActiveGcRecord,
            CreatedBy = nameof(FilteredUniqueIndexesIntegrationTests)
        };
        seedDb.Companies.Add(company);
        await seedDb.SaveChangesAsync();
        return company.Id;
    }

    private async Task<Guid> SeedCountryAsync()
    {
        await using var seedScope = _factory.Services.CreateAsyncScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // Unique IsoCode per call: every test in this class shares one Testcontainers database.
        var country = new Country
        {
            Name = $"Testland-{Guid.NewGuid():N}".Substring(0, 24),
            IsoCode = $"T{Guid.NewGuid():N}".Substring(0, 10),
            GcRecord = BaseAuditableEntity.ActiveGcRecord,
            CreatedBy = nameof(FilteredUniqueIndexesIntegrationTests)
        };
        seedDb.Countries.Add(country);
        await seedDb.SaveChangesAsync();
        return country.Id;
    }

    private async Task<Guid> SeedIdentificationTypeAsync()
    {
        await using var seedScope = _factory.Services.CreateAsyncScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var identificationType = new IdentificationType
        {
            Name = $"IT-{Guid.NewGuid():N}".Substring(0, 20),
            IsActive = true,
            GcRecord = BaseAuditableEntity.ActiveGcRecord,
            CreatedBy = nameof(FilteredUniqueIndexesIntegrationTests)
        };
        seedDb.IdentificationTypes.Add(identificationType);
        await seedDb.SaveChangesAsync();
        return identificationType.Id;
    }

    private async Task<Guid> SeedPersonAsync(Guid companyId, Guid? identificationTypeId = null)
    {
        var typeId = identificationTypeId ?? await SeedIdentificationTypeAsync();
        await using var scope = CreateScopeAsCompany(companyId);
        var person = new Person
        {
            CompanyId = companyId,
            PersonType = PersonType.Legal,
            FirstName = "Juan",
            LastName = "Perez",
            IdentificationTypeId = typeId,
            GcRecord = BaseAuditableEntity.ActiveGcRecord,
            CreatedBy = nameof(FilteredUniqueIndexesIntegrationTests)
        };
        person.IdentificationNumber = $"ID-{person.Id:N}".Substring(0, 20);
        scope.Context.Persons.Add(person);
        await scope.Context.SaveChangesAsync();
        return person.Id;
    }

    private sealed class TestScope : IAsyncDisposable
    {
        public TestScope(IServiceScope scope, ApplicationDbContext context)
        {
            Scope = scope;
            Context = context;
        }

        public IServiceScope Scope { get; }
        public ApplicationDbContext Context { get; }

        public ValueTask DisposeAsync()
        {
            Scope.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// EF-only scope with the tenant set through <see cref="TestCurrentUserService.CompanyIdOverride"/>
    /// so the SPEC 38 global query filters see the right company (no HttpContext here).
    /// </summary>
    private TestScope CreateScopeAsCompany(Guid companyId)
    {
        var scope = _factory.Services.CreateAsyncScope();
        var testCurrentUser = scope.ServiceProvider.GetRequiredService<TestCurrentUserService>();
        testCurrentUser.CompanyIdOverride = companyId;
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return new TestScope(scope, context);
    }

    /// <summary>
    /// Registers a fresh user linked to <paramref name="companyId"/> with the <c>SuperAdmin</c>
    /// role (so <c>DynamicAuthorizationFilter</c> bypasses its permission check) and returns an
    /// <see cref="HttpClient"/> carrying the bearer token whose <c>CompanyId</c> claim is
    /// <paramref name="companyId"/>.
    /// </summary>
    private async Task<HttpClient> CreateAuthenticatedClientAsync(Guid companyId)
    {
        const string password = "Integration!Pass123";
        var email = $"u{Guid.NewGuid():N}@t.local";

        using var unauthClient = _factory.CreateClient();
        var registerResponse = await unauthClient.PostAsJsonAsync(
            "/api/v1/users/register",
            new RegisterCommand
            {
                Email = email,
                Password = password,
                FirstName = "Test",
                LastName = "User",
            });
        registerResponse.EnsureSuccessStatusCode();

        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = seedScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var registeredUser = await userManager.FindByEmailAsync(email);
            registeredUser.Should().NotBeNull();

            seedDb.UserCompanies.Add(new UserCompany
            {
                UserId = registeredUser!.Id,
                CompanyId = companyId,
                IsDefault = true,
                GcRecord = BaseAuditableEntity.ActiveGcRecord,
                CreatedBy = nameof(FilteredUniqueIndexesIntegrationTests)
            });
            (await userManager.AddToRoleAsync(registeredUser, "SuperAdmin"))
                .Succeeded.Should().BeTrue();
            await seedDb.SaveChangesAsync();
        }

        var loginResponse = await unauthClient.PostAsJsonAsync(
            "/api/v1/users/login",
            new LoginCommand { Email = email, Password = password });
        loginResponse.EnsureSuccessStatusCode();

        var loginPayload = await loginResponse.Content.ReadFromJsonAsync<Response<LoginResponse>>();
        loginPayload!.Data!.Token.Should().NotBeNullOrWhiteSpace();

        var authClient = _factory.CreateClient();
        authClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginPayload.Data.Token);
        authClient.DefaultRequestHeaders.Add("X-Company-Id", companyId.ToString());

        return authClient;
    }
}
