using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AutoFixture;
using FluentAssertions;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Application.UseCases.Security.Auth.Register;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JOIN.IntegrationTests.Admin;

/// <summary>
/// SPEC 50 (F2) — verifies the <c>DeclaredDate</c> timezone fix end to end: a JSON payload
/// with a UTC timestamp (terminated in <c>Z</c>) must round-trip through Npgsql without
/// rejecting the <c>Kind=Utc</c> value on a <c>timestamp without time zone</c> column.
/// Setup mirrors the smoke test: register, attach to a seeded company with the SuperAdmin
/// role, then insert a Person and IncomeRange directly through the DbContext so the
/// scenario exercises only the DeclaredDate write path.
/// </summary>
[Collection("IntegrationTests")]
public sealed class PersonFinancialProfilePostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "Str0ng!Pass2026";

    private readonly CustomWebApplicationFactory _factory;
    private readonly Fixture _fixture = new();

    public PersonFinancialProfilePostgreSqlTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreatePersonFinancialProfile_WithUtcTimestampEndingInZ_ReturnsCreated()
    {
        var (authClient, personId, incomeRangeId) = await BootstrapAsync();

        var payload = new
        {
            PersonId = personId,
            IncomeRangeId = incomeRangeId,
            SourceOfFunds = "Salary",
            DeclaredDate = "2026-01-15T10:00:00.000Z",
            IsCurrent = true,
            IsActive = true
        };
        var json = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await authClient.PostAsync("/api/v1/personFinancialProfile", json);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<(HttpClient AuthClient, Guid PersonId, Guid IncomeRangeId)> BootstrapAsync()
    {
        var unauthClient = _factory.CreateClient();
        var email = $"{_fixture.Create<string>()}@integration.test";

        var registerResponse = await unauthClient.PostAsJsonAsync(
            "/api/v1/users/register",
            new RegisterCommand
            {
                Email = email,
                Password = Password,
                FirstName = _fixture.Create<string>(),
                LastName = _fixture.Create<string>(),
            });
        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK, await registerResponse.Content.ReadAsStringAsync());

        Guid companyId;
        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = seedScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var company = await seedDb.Companies
                .Where(c => c.IsActive && c.GcRecord == 0)
                .OrderBy(c => c.Created)
                .FirstOrDefaultAsync();
            company.Should().NotBeNull();
            companyId = company!.Id;

            var registeredUser = await userManager.FindByEmailAsync(email);
            registeredUser.Should().NotBeNull();
            seedDb.UserCompanies.Add(new UserCompany
            {
                UserId = registeredUser!.Id,
                CompanyId = companyId,
                IsDefault = true,
                GcRecord = BaseAuditableEntity.ActiveGcRecord,
                CreatedBy = nameof(PersonFinancialProfilePostgreSqlTests),
            });
            (await userManager.AddToRoleAsync(registeredUser, "SuperAdmin")).Succeeded.Should().BeTrue();
            await seedDb.SaveChangesAsync();
        }

        var loginResponse = await unauthClient.PostAsJsonAsync(
            "/api/v1/users/login",
            new LoginCommand { Email = email, Password = Password });
        var loginRaw = await loginResponse.Content.ReadAsStringAsync();
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK, loginRaw);

        var loginPayload = JsonDocument.Parse(loginRaw).RootElement;
        var token = loginPayload.GetProperty("data").GetProperty("token").GetString();
        token.Should().NotBeNullOrWhiteSpace();

        var authClient = _factory.CreateClient();
        authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        authClient.DefaultRequestHeaders.Add("X-Company-Id", companyId.ToString());

        Guid personId, incomeRangeId;
        await using (var dataScope = _factory.Services.CreateAsyncScope())
        {
            var db = dataScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var incomeRange = JOIN.Domain.Admin.IncomeRange.Create(
                companyId,
                "Test range",
                0m,
                1000m,
                "USD",
                999);
            db.IncomeRanges.Add(incomeRange);

            var identificationType = await db.IdentificationTypes
                .Where(t => t.GcRecord == 0)
                .OrderBy(t => t.Created)
                .FirstOrDefaultAsync();
            identificationType.Should().NotBeNull();

            var person = new JOIN.Domain.Admin.Person
            {
                CompanyId = companyId,
                FirstName = "F2",
                LastName = "Integration",
                IdentificationTypeId = identificationType!.Id,
                IdentificationNumber = $"F2INT-{Guid.NewGuid():N}",
                GcRecord = BaseAuditableEntity.ActiveGcRecord,
                CreatedBy = nameof(PersonFinancialProfilePostgreSqlTests),
            };
            db.Persons.Add(person);
            await db.SaveChangesAsync();

            incomeRangeId = incomeRange.Id;
            personId = person.Id;
        }

        return (authClient, personId, incomeRangeId);
    }
}
