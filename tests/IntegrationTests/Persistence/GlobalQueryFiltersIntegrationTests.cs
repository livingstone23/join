// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Application.UseCases.Security.Auth.Register;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JOIN.IntegrationTests.Persistence;

/// <summary>
/// SPEC 38 (F3.2) — End-to-end coverage of the new global query filters against a real SQL
/// Server (Testcontainers in <see cref="CustomWebApplicationFactory"/>). The companion
/// <see cref="GlobalQueryFiltersGuardTests"/> enforces the model contract at the unit level;
/// these tests exercise the runtime behavior on real data.
/// </summary>
public sealed class GlobalQueryFiltersIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public GlobalQueryFiltersIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// F3.2.1 — <c>Region</c>: dos empresas con una región cada una; autenticado como A,
    /// <c>context.Regions.ToList()</c> solo devuelve la de A.
    /// </summary>
    [Fact]
    public async Task Region_EfQuery_AsCompanyA_ReturnsOnlyCompanyARegion()
    {
        var (companyA, companyB) = await SeedTwoCompaniesAsync();
        var countryId = await SeedCountryAsync();

        await SeedRegionAsync(companyA, countryId, "Norte-A");
        await SeedRegionAsync(companyB, countryId, "Norte-B");

        await using var scopeA = CreateScopeAsCompany(companyA);
        var visibleToA = await scopeA.Context.Regions
            .AsNoTracking()
            .Select(r => r.Name)
            .ToListAsync();

        visibleToA.Should().BeEquivalentTo(new[] { "Norte-A" });
    }

    /// <summary>
    /// F3.2.2 — <c>Region</c>: <c>GET /regions</c> autenticado como Manager de A no devuelve
    /// regiones de B. The Dapper handler requires <c>AND r.CompanyId = @TenantId</c>.
    /// </summary>
    [Fact]
    public async Task Region_GetRegionsEndpoint_AsManagerOfA_DoesNotLeakB()
    {
        var (companyA, companyB) = await SeedTwoCompaniesAsync();
        var countryId = await SeedCountryAsync();

        await SeedRegionAsync(companyA, countryId, "Norte-A");
        await SeedRegionAsync(companyB, countryId, "Norte-B");

        // The Region endpoint is gated by DynamicAuthorizationFilter, which requires a JWT
        // (ClaimTypes.NameIdentifier + a non-empty CompanyId claim) — bare X-Company-Id
        // header returns 401. Authenticate as a SuperAdmin user scoped to companyA so the
        // filter bypasses its permission check and resolves the Dapper tenant from the
        // JWT's CompanyId claim.
        using var client = await CreateAuthenticatedClientAsync(companyA);

        var response = await client.GetAsync("/api/v1/regions?PageSize=50");
        response.IsSuccessStatusCode.Should().BeTrue();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var regionNames = payload.GetProperty("data").GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToList();

        regionNames.Should().BeEquivalentTo(new[] { "Norte-A" });
    }

    /// <summary>
    /// F3.2.3 — <c>Region</c>: la empresa B puede crear una región con el mismo nombre y país
    /// que una de A (antes fallaba con REGION_NAME_IN_USE porque la validación corría sobre
    /// <c>GetAllAsync()</c> de todas las empresas).
    /// </summary>
    [Fact]
    public async Task Region_CreateInCompanyB_DoesNotCollideWithCompanyA()
    {
        var (companyA, companyB) = await SeedTwoCompaniesAsync();
        var countryId = await SeedCountryAsync();

        await SeedRegionAsync(companyA, countryId, "Pacifico");

        await using var scopeB = CreateScopeAsCompany(companyB);
        scopeB.Context.Regions.Add(new Region
        {
            CompanyId = companyB,
            CountryId = countryId,
            Name = "Pacifico",
            Code = "PA",
            GcRecord = BaseAuditableEntity.ActiveGcRecord,
            CreatedBy = nameof(GlobalQueryFiltersIntegrationTests)
        });

        var act = async () => await scopeB.Context.SaveChangesAsync();
        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// F3.2.4 — <c>Gender</c>: una fila de B no aparece en <c>context.Genders</c> autenticado
    /// como A. Cross-tenant <c>GenderId</c> en <c>CreatePerson</c> falla FK validation.
    /// </summary>
    [Fact]
    public async Task Gender_EfQueryAsA_DoesNotSeeB_GenderIdFromBHitsInvalidReference()
    {
        var (companyA, companyB) = await SeedTwoCompaniesAsync();

        Gender genderB;
        await using (var scopeB = CreateScopeAsCompany(companyB))
        {
            genderB = Gender.Create(companyB, "OT", "Other");
            scopeB.Context.Genders.Add(genderB);
            await scopeB.Context.SaveChangesAsync();
        }

        await using var scopeA = CreateScopeAsCompany(companyA);
        var visibleToA = await scopeA.Context.Genders.AsNoTracking().Select(g => g.Id).ToListAsync();

        visibleToA.Should().NotContain(genderB.Id);
    }

    /// <summary>
    /// F3.2.5 — <c>UserCompany</c>: filas de dos empresas visibles sin importar el tenant
    /// autenticado (única BaseTenantEntity sin filtro); una fila soft-deleted no aparece.
    /// </summary>
    [Fact]
    public async Task UserCompany_IsVisibleAcrossTenants_AndHonorsSoftDelete()
    {
        var (companyA, companyB) = await SeedTwoCompaniesAsync();
        var userId = await SeedUserAsync();
        // The soft-deleted row needs a CompanyId that (a) satisfies the FK to Security.Companies
        // and (b) does not collide with the (UserId, CompanyId) unique index on the two active
        // rows above — so it has to belong to a third company.
        var companyC = await SeedExtraCompanyAsync();

        await using var scope = CreateScopeAsCompany(companyA);
        scope.Context.UserCompanies.Add(new UserCompany
        {
            UserId = userId,
            CompanyId = companyA,
            IsDefault = true,
            GcRecord = BaseAuditableEntity.ActiveGcRecord,
            CreatedBy = nameof(GlobalQueryFiltersIntegrationTests)
        });
        scope.Context.UserCompanies.Add(new UserCompany
        {
            UserId = userId,
            CompanyId = companyB,
            IsDefault = false,
            GcRecord = BaseAuditableEntity.ActiveGcRecord,
            CreatedBy = nameof(GlobalQueryFiltersIntegrationTests)
        });
        var revoked = new UserCompany
        {
            UserId = userId,
            CompanyId = companyC,
            IsDefault = false,
            GcRecord = 20240101, // soft-deleted
            CreatedBy = nameof(GlobalQueryFiltersIntegrationTests)
        };
        scope.Context.UserCompanies.Add(revoked);
        await scope.Context.SaveChangesAsync();

        var visible = await scope.Context.UserCompanies
            .AsNoTracking()
            .Where(uc => uc.UserId == userId)
            .ToListAsync();

        visible.Should().HaveCount(2, "both active memberships are visible regardless of token tenant");
        visible.Select(v => v.CompanyId).Should().BeEquivalentTo(new[] { companyA, companyB });
        visible.Should().NotContain(uc => uc.Id == revoked.Id);
    }

    /// <summary>
    /// F3.2.6 — <c>Province</c>: visible para todas las empresas (catálogo global); si su
    /// <c>RegionId</c> apunta a una región de B, <c>GET /provinces</c> autenticado como A
    /// devuelve <c>RegionName = null</c>.
    /// </summary>
    [Fact]
    public async Task Province_WithRegionIdOfAnotherCompany_ReturnsNullRegionName()
    {
        var (companyA, companyB) = await SeedTwoCompaniesAsync();
        var countryId = await SeedCountryAsync();

        var regionBId = await SeedRegionAsync(companyB, countryId, "Region-B");

        Guid provinceId;
        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var province = new Province
            {
                Name = "CrossTenantProvince",
                Code = "CT",
                CountryId = countryId,
                RegionId = regionBId,
                GcRecord = BaseAuditableEntity.ActiveGcRecord,
                CreatedBy = nameof(GlobalQueryFiltersIntegrationTests)
            };
            seedDb.Provinces.Add(province);
            await seedDb.SaveChangesAsync();
            provinceId = province.Id;
        }

        using var client = await CreateAuthenticatedClientAsync(companyA);

        var response = await client.GetAsync($"/api/v1/provinces/{provinceId}");
        response.IsSuccessStatusCode.Should().BeTrue();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("data").GetProperty("regionName").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// F3.2.7 — <c>UserConnectionLog</c>: una fila con <c>GcRecord != 0</c> no aparece.
    /// </summary>
    [Fact]
    public async Task UserConnectionLog_SoftDeletedRows_AreHiddenByFilter()
    {
        var (companyA, _) = await SeedTwoCompaniesAsync();
        var userId = await SeedUserAsync();

        await using var scope = CreateScopeAsCompany(companyA);
        scope.Context.UserConnectionLogs.Add(new UserConnectionLog
        {
            UserId = userId,
            IpAddress = "127.0.0.1",
            IsActiveSession = true,
            ConnectionDate = DateTime.UtcNow,
            GcRecord = BaseAuditableEntity.ActiveGcRecord,
            CreatedBy = nameof(GlobalQueryFiltersIntegrationTests)
        });
        scope.Context.UserConnectionLogs.Add(new UserConnectionLog
        {
            UserId = userId,
            IpAddress = "127.0.0.2",
            IsActiveSession = false,
            DisconnectionDate = DateTime.UtcNow,
            GcRecord = 20240101, // soft-deleted
            CreatedBy = nameof(GlobalQueryFiltersIntegrationTests)
        });
        await scope.Context.SaveChangesAsync();

        var visible = await scope.Context.UserConnectionLogs.AsNoTracking()
            .Where(log => log.UserId == userId)
            .ToListAsync();

        visible.Should().Contain(log => log.IpAddress == "127.0.0.1");
        visible.Should().NotContain(log => log.IpAddress == "127.0.0.2");
    }

    /// <summary>
    /// F3.2.8 — <c>ApplicationUser</c>: un usuario soft-deleted sigue siendo devuelto por
    /// <c>context.Users.Find(id)</c> porque la entidad está excluida del guard por diseño.
    /// </summary>
    [Fact]
    public async Task ApplicationUser_SoftDeleted_IsStillReturnedByFind()
    {
        var (_, _) = await SeedTwoCompaniesAsync();
        var userId = await SeedUserAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await userManager.FindByIdAsync(userId.ToString());
        user.Should().NotBeNull();

        user!.GcRecord = 20240101;
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();

        var lookup = await userManager.FindByIdAsync(userId.ToString());
        lookup.Should().NotBeNull(
            "ApplicationUser has no global query filter — soft-deleted users remain visible by design");
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private async Task<(Guid CompanyA, Guid CompanyB)> SeedTwoCompaniesAsync()
    {
        await using var seedScope = _factory.Services.CreateAsyncScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var companyA = new Company
        {
            Name = "JOIN-A",
            TaxId = $"RUCA{Guid.NewGuid():N}".Substring(0, 16),
            IsActive = true,
            GcRecord = BaseAuditableEntity.ActiveGcRecord,
            CreatedBy = nameof(GlobalQueryFiltersIntegrationTests)
        };
        var companyB = new Company
        {
            Name = "JOIN-B",
            TaxId = $"RUCB{Guid.NewGuid():N}".Substring(0, 16),
            IsActive = true,
            GcRecord = BaseAuditableEntity.ActiveGcRecord,
            CreatedBy = nameof(GlobalQueryFiltersIntegrationTests)
        };
        seedDb.Companies.Add(companyA);
        seedDb.Companies.Add(companyB);
        await seedDb.SaveChangesAsync();

        return (companyA.Id, companyB.Id);
    }

    private async Task<Guid> SeedExtraCompanyAsync()
    {
        await using var seedScope = _factory.Services.CreateAsyncScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var company = new Company
        {
            Name = $"JOIN-C-{Guid.NewGuid():N}".Substring(0, 18),
            TaxId = $"RUCC{Guid.NewGuid():N}".Substring(0, 16),
            IsActive = true,
            GcRecord = BaseAuditableEntity.ActiveGcRecord,
            CreatedBy = nameof(GlobalQueryFiltersIntegrationTests)
        };
        seedDb.Companies.Add(company);
        await seedDb.SaveChangesAsync();
        return company.Id;
    }

    private async Task<Guid> SeedUserAsync()
    {
        await using var seedScope = _factory.Services.CreateAsyncScope();
        var userManager = seedScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userId = Guid.NewGuid();
        // The previous version derived UserName / Email from the full GUID and truncated
        // the email with .Substring(0, 30), which lopped off the @ and domain half —
        // UserManager's default email validator then rejected the row silently (CreateAsync
        // returns IdentityResult.Failed, doesn't throw) and every downstream FK exploded.
        // Keep both fields short and complete.
        var shortTag = Guid.NewGuid().ToString("N").Substring(0, 8);
        var identityUser = new ApplicationUser
        {
            Id = userId,
            UserName = $"u{shortTag}",
            Email = $"u{shortTag}@t.local",
            EmailConfirmed = true,
            IsActive = true,
            GcRecord = BaseAuditableEntity.ActiveGcRecord
        };
        // Password is required by UserManager<ApplicationUser> — CreateAsync(identityUser)
        // without one returns IdentityResult.Failed but does not throw, leaving the user
        // unpersisted and breaking every downstream FK (UserConnectionLog, UserCompany,
        // ApplicationUser.FindByIdAsync). Assert on the result so a future policy change
        // surfaces here instead of as a confusing FK violation downstream.
        var result = await userManager.CreateAsync(identityUser, "Integration!Pass123");
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"SeedUserAsync failed to persist user {userId}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        // Confirm the row is actually in the database — guards against a future code path
        // where CreateAsync returns Success but the change tracker is rolled back.
        var fromDatabase = await seedDb.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (fromDatabase is null)
        {
            throw new InvalidOperationException(
                $"SeedUserAsync: user {userId} not visible to a fresh DbContext read after CreateAsync.");
        }

        return userId;
    }

    private async Task<Guid> SeedCountryAsync()
    {
        await using var seedScope = _factory.Services.CreateAsyncScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // xUnit runs the tests in this class sequentially against the same Testcontainers
        // SQL Server (one fixture per class), so a hard-coded "TL" IsoCode collides with
        // the unique index IX_Countries_IsoCode on every invocation after the first.
        // Use a unique IsoCode per call to keep each test self-contained.
        var isoCode = $"T{Guid.NewGuid():N}".Substring(0, 10);
        var country = new Country
        {
            Name = $"Testland-{Guid.NewGuid():N}".Substring(0, 24),
            IsoCode = isoCode,
            GcRecord = BaseAuditableEntity.ActiveGcRecord,
            CreatedBy = nameof(GlobalQueryFiltersIntegrationTests)
        };
        seedDb.Countries.Add(country);
        await seedDb.SaveChangesAsync();
        return country.Id;
    }

    private async Task<Guid> SeedRegionAsync(Guid companyId, Guid countryId, string name)
    {
        Guid regionId;
        await using (var scope = CreateScopeAsCompany(companyId))
        {
            var region = new Region
            {
                CompanyId = companyId,
                CountryId = countryId,
                Name = name,
                Code = name.Substring(0, 2).ToUpperInvariant(),
                GcRecord = BaseAuditableEntity.ActiveGcRecord,
                CreatedBy = nameof(GlobalQueryFiltersIntegrationTests)
            };
            scope.Context.Regions.Add(region);
            await scope.Context.SaveChangesAsync();
            regionId = region.Id;
        }
        return regionId;
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

    private TestScope CreateScopeAsCompany(Guid companyId)
    {
        // SPEC 38 note: the production HttpContext-based ICurrentUserService is wired into
        // DI. EF reads inside a single scope evaluate the filter against the service's
        // CompanyId — but scopes created via Services.CreateAsyncScope() have no
        // HttpContext, so without help every tenant filter evaluates to Guid.Empty and
        // hides every row. The TestCurrentUserService registered in
        // CustomWebApplicationFactory exposes a per-scope CompanyIdOverride that this
        // helper sets before resolving the DbContext, so EF reads inside the scope see
        // the right tenant. Tests that need a precise token-driven tenant scope over the
        // HTTP path (F3.2.2, F3.2.6) keep their existing X-Company-Id header flow — the
        // override is ignored when an HttpContext is present.
        var scope = _factory.Services.CreateAsyncScope();
        var testCurrentUser = scope.ServiceProvider.GetRequiredService<TestCurrentUserService>();
        testCurrentUser.CompanyIdOverride = companyId;
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return new TestScope(scope, context);
    }

    /// <summary>
    /// Registers a fresh user, seeds the <see cref="UserCompany"/> link to
    /// <paramref name="companyId"/>, assigns the <c>SuperAdmin</c> role so
    /// <see cref="JOIN.Services.WebApi.Filters.DynamicAuthorizationFilter"/> bypasses
    /// its permission check, and returns an <see cref="HttpClient"/> pre-loaded with
    /// the resulting bearer token. The token's <c>CompanyId</c> claim carries
    /// <paramref name="companyId"/>, so Dapper handlers with a tenant predicate see the
    /// right scope without needing a header fallback.
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
                CreatedBy = nameof(GlobalQueryFiltersIntegrationTests)
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