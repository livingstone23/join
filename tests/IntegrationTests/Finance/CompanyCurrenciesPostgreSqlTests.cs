// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Application.UseCases.Security.Auth.Register;
using JOIN.Domain.Security;
using JOIN.IntegrationTests.Security;
using JOIN.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace JOIN.IntegrationTests.Finance;

/// <summary>
/// SPEC 56 — base currency per company, parallel currencies and dated exchange rates against PostgreSQL 17.
/// Each test that changes a base works on its own company (created with <c>POST /Companies</c>) and a SuperAdmin
/// bound to it, so JOIN-001 keeps USD for the rest of the suite.
/// </summary>
public sealed class CompanyCurrenciesPostgreSqlTests : IClassFixture<FinancePostgreSqlWebApplicationFactory>
{
    private const string Password = "Integration!Pass123";
    private readonly FinancePostgreSqlWebApplicationFactory _factory;

    public CompanyCurrenciesPostgreSqlTests(FinancePostgreSqlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ExistingAndNewCompanies_HaveDopAsTheirOnlyBase()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var bases = await (from cc in db.CompanyCurrencies.IgnoreQueryFilters()
                               join c in db.Currencies.IgnoreQueryFilters() on cc.CurrencyId equals c.Id
                               where cc.IsBase && cc.GcRecord == 0
                               select new { cc.CompanyId, c.Code }).ToListAsync();
            var companies = await db.Companies.IgnoreQueryFilters().Where(c => c.GcRecord == 0).Select(c => c.Id).ToListAsync();

            bases.GroupBy(b => b.CompanyId).Should().OnlyContain(g => g.Count() == 1);
            bases.Select(b => b.CompanyId).Should().BeEquivalentTo(companies);
            bases.Should().OnlyContain(b => b.Code == "USD");
        }

        var (companyId, _) = await CreateCompanyWithAdminAsync();
        (await BaseCodeAsync(companyId)).Should().Be("USD", "a company created with POST /Companies is born with USD");
    }

    [Fact]
    public async Task MarkingAnotherBase_MovesIt_AndConcurrentRequestsNeverLeaveTwoBases()
    {
        var (companyId, client) = await CreateCompanyWithAdminAsync();

        var dop = await client.PostAsJsonAsync("/api/v1/CompanyCurrencies", new { currencyId = await CurrencyIdAsync("DOP"), isBase = true });
        dop.StatusCode.Should().Be(HttpStatusCode.Created, await dop.Content.ReadAsStringAsync());
        (await BaseCodeAsync(companyId)).Should().Be("DOP");

        var eurId = await CurrencyIdAsync("EUR");
        var copId = await CurrencyIdAsync("COP");
        await Task.WhenAll(
            client.PostAsJsonAsync("/api/v1/CompanyCurrencies", new { currencyId = eurId, isBase = true }),
            client.PostAsJsonAsync("/api/v1/CompanyCurrencies", new { currencyId = copId, isBase = true }));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.CompanyCurrencies.IgnoreQueryFilters().CountAsync(c => c.CompanyId == companyId && c.IsBase && c.GcRecord == 0))
            .Should().Be(1);
    }

    [Fact]
    public async Task TheBase_CannotBeDeactivatedNorDeleted()
    {
        var (_, client) = await CreateCompanyWithAdminAsync();
        var usd = await CompanyCurrencyAsync(client, "USD");

        var deactivate = await client.PutAsJsonAsync($"/api/v1/CompanyCurrencies/{usd.GetProperty("id").GetGuid()}", new { isBase = true, isActive = false });
        var delete = await client.DeleteAsync($"/api/v1/CompanyCurrencies/{usd.GetProperty("id").GetGuid()}");

        deactivate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await deactivate.Content.ReadAsStringAsync()).Should().Contain("BASE_CURRENCY_REQUIRED");
        delete.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await delete.Content.ReadAsStringAsync()).Should().Contain("BASE_CURRENCY_REQUIRED");
    }

    [Fact]
    public async Task WithAmounts_ChangingTheBase_Returns409()
    {
        var (companyId, client) = await CreateCompanyWithAdminAsync();
        lock (_factory.AmountSource.CompaniesWithAmounts)
        {
            _factory.AmountSource.CompaniesWithAmounts.Add(companyId);
        }

        var response = await client.PostAsJsonAsync("/api/v1/CompanyCurrencies", new { currencyId = await CurrencyIdAsync("DOP"), isBase = true });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("BASE_CURRENCY_IN_USE").And.Contain("TestAmounts");
        (await BaseCodeAsync(companyId)).Should().Be("USD");
    }

    [Fact]
    public async Task Resolve_ReturnsTheBaseTheExactRateTheLatestEarlierOrNotFound()
    {
        var (_, client) = await CreateCompanyWithAdminAsync();
        var dopId = await CurrencyIdAsync("DOP");
        var eurId = await CurrencyIdAsync("EUR");
        (await client.PostAsJsonAsync("/api/v1/CompanyCurrencies", new { currencyId = dopId })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/v1/CompanyCurrencies", new { currencyId = eurId })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/v1/ExchangeRates", new { currencyId = dopId, rateDate = "2026-10-10", rate = 59.10m })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/v1/ExchangeRates", new { currencyId = dopId, rateDate = "2026-10-14", rate = 59.36m })).EnsureSuccessStatusCode();
        var usdId = await CurrencyIdAsync("USD");

        var baseRate = Data(await client.GetStringAsync($"/api/v1/ExchangeRates/resolve?currencyId={usdId}&date=2026-10-16"));
        baseRate.GetProperty("rate").GetDecimal().Should().Be(1m);
        baseRate.GetProperty("isBase").GetBoolean().Should().BeTrue();

        var exact = Data(await client.GetStringAsync($"/api/v1/ExchangeRates/resolve?currencyId={dopId}&date=2026-10-10"));
        exact.GetProperty("rate").GetDecimal().Should().Be(59.10m);

        var fallback = Data(await client.GetStringAsync($"/api/v1/ExchangeRates/resolve?currencyId={dopId}&date=2026-10-16"));
        fallback.GetProperty("rate").GetDecimal().Should().Be(59.36m);
        fallback.GetProperty("rateDate").GetString().Should().Be("2026-10-14");

        var none = await client.GetAsync($"/api/v1/ExchangeRates/resolve?currencyId={eurId}&date=2026-10-16");
        none.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await none.Content.ReadAsStringAsync()).Should().Contain("EXCHANGE_RATE_NOT_FOUND");
    }

    [Fact]
    public async Task Rates_ForTheBaseOrOnATakenDate_AreRejected()
    {
        var (_, client) = await CreateCompanyWithAdminAsync();
        var dopId = await CurrencyIdAsync("DOP");
        (await client.PostAsJsonAsync("/api/v1/CompanyCurrencies", new { currencyId = dopId })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/v1/ExchangeRates", new { currencyId = dopId, rateDate = "2026-10-16", rate = 59.36m })).EnsureSuccessStatusCode();

        var forBase = await client.PostAsJsonAsync("/api/v1/ExchangeRates", new { currencyId = await CurrencyIdAsync("USD"), rateDate = "2026-10-16", rate = 1m });
        var duplicate = await client.PostAsJsonAsync("/api/v1/ExchangeRates", new { currencyId = dopId, rateDate = "2026-10-16", rate = 60m });

        forBase.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await forBase.Content.ReadAsStringAsync()).Should().Contain("EXCHANGE_RATE_FOR_BASE");
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GlobalCatalog_IsSuperAdminOnly_AndCompanyDataIsIsolated()
    {
        var (_, other) = await CreateCompanyWithAdminAsync();
        var dopId = await CurrencyIdAsync("DOP");
        (await other.PostAsJsonAsync("/api/v1/CompanyCurrencies", new { currencyId = dopId })).EnsureSuccessStatusCode();
        (await other.PostAsJsonAsync("/api/v1/ExchangeRates", new { currencyId = dopId, rateDate = "2026-01-02", rate = 58.77m })).EnsureSuccessStatusCode();

        using var manager = await ModuleTestHelpers.LoginAsync(_factory, "manager@join.com");
        (await manager.GetAsync("/api/v1/Currencies")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var superAdmin = await ModuleTestHelpers.LoginAsync(_factory, "lcano@join.com");
        (await superAdmin.GetAsync("/api/v1/Currencies")).StatusCode.Should().Be(HttpStatusCode.OK);

        var joinRates = await manager.GetStringAsync("/api/v1/ExchangeRates?pageSize=50");
        joinRates.Should().NotContain("58.77", "the rate belongs to another company");
        var joinCurrencies = Data(await manager.GetStringAsync("/api/v1/CompanyCurrencies"));
        joinCurrencies.EnumerateArray().Should().OnlyContain(c => c.GetProperty("companyId").GetGuid() != Guid.Empty);
        joinCurrencies.EnumerateArray().Count(c => c.GetProperty("isBase").GetBoolean()).Should().Be(1);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> CurrencyIdAsync(string code)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Currencies.IgnoreQueryFilters().Where(c => c.Code == code && c.GcRecord == 0).Select(c => c.Id).SingleAsync();
    }

    private async Task<string> BaseCodeAsync(Guid companyId)
    {
        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT c.code FROM finance.companycurrencies cc
            JOIN finance.currencies c ON c.id = cc.currencyid
            WHERE cc.companyid = @company AND cc.isbase = TRUE AND cc.gcrecord = 0
            """, conn);
        cmd.Parameters.AddWithValue("company", companyId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<JsonElement> CompanyCurrencyAsync(HttpClient client, string code)
    {
        var list = Data(await client.GetStringAsync("/api/v1/CompanyCurrencies"));
        return list.EnumerateArray().Single(c => c.GetProperty("code").GetString() == code);
    }

    /// <summary>
    /// Creates a company with <c>POST /Companies</c> (as lcano@join.com) and a fresh SuperAdmin bound to it.
    /// </summary>
    private async Task<(Guid CompanyId, HttpClient Client)> CreateCompanyWithAdminAsync()
    {
        using var superAdmin = await ModuleTestHelpers.LoginAsync(_factory, "lcano@join.com");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var created = await superAdmin.PostAsJsonAsync("/api/v1/Companies", new { name = $"FIN-{suffix}", taxId = $"FIN{suffix}", isActive = true });
        var body = await created.Content.ReadAsStringAsync();
        created.IsSuccessStatusCode.Should().BeTrue(body);
        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data");
        var companyId = data.ValueKind == JsonValueKind.String ? data.GetGuid() : data.GetProperty("id").GetGuid();

        var email = $"fin-{suffix}@t.local";
        using var anonymous = _factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/users/register", new RegisterCommand { Email = email, Password = Password, FirstName = "Fin", LastName = "Admin" }))
            .EnsureSuccessStatusCode();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            db.UserCompanies.Add(new UserCompany { UserId = user.Id, CompanyId = companyId, IsDefault = true, CreatedBy = "spec56-tests" });
            (await users.AddToRoleAsync(user, "SuperAdmin")).Succeeded.Should().BeTrue();
            await db.SaveChangesAsync();
        }

        var login = await anonymous.PostAsJsonAsync("/api/v1/users/login", new LoginCommand { Email = email, Password = Password, TargetCompanyId = companyId });
        login.EnsureSuccessStatusCode();
        var payload = (await login.Content.ReadFromJsonAsync<Response<LoginResponse>>())!.Data!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.Token);
        return (companyId, client);
    }

    private static JsonElement Data(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").Clone();
    }
}
