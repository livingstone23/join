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

namespace JOIN.IntegrationTests.Catalog;

/// <summary>
/// SPEC 57 — product catalog per company against PostgreSQL 17: unique normalized code, lookup, categories and the
/// base-currency lock once a product has a price (real <c>ProductReferencePriceAmountSource</c>).
/// </summary>
public sealed class ProductCatalogPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "Integration!Pass123";
    private readonly CustomWebApplicationFactory _factory;

    public ProductCatalogPostgreSqlTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Code_IsNormalized_AndUniquePerCompany()
    {
        var (_, first) = await CreateCompanyWithAdminAsync();
        var (_, second) = await CreateCompanyWithAdminAsync();

        var created = await first.PostAsJsonAsync("/api/v1/Products", new { code = " crm-usr ", name = "Licencia", kind = 2, referencePrice = 1450m });
        var body = await created.Content.ReadAsStringAsync();
        created.StatusCode.Should().Be(HttpStatusCode.Created, body);
        Data(body).GetProperty("code").GetString().Should().Be("CRM-USR");
        Data(body).GetProperty("baseCurrencyCode").GetString().Should().Be("DOP");

        (await first.PostAsJsonAsync("/api/v1/Products", new { code = "CRM-USR", name = "Otra", kind = 2 })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.PostAsJsonAsync("/api/v1/Products", new { code = "CRM-USR", name = "Licencia", kind = 2 })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Lookup_ReturnsOnlyActiveProductsOfTheCompany_CaseInsensitive()
    {
        var (_, client) = await CreateCompanyWithAdminAsync();
        (await client.PostAsJsonAsync("/api/v1/Products", new { code = "LIC-A", name = "Licencia Anual", kind = 2 })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/v1/Products", new { code = "LIC-B", name = "Licencia vieja", kind = 2, isActive = false })).EnsureSuccessStatusCode();

        var items = Data(await client.GetStringAsync("/api/v1/Products/lookup?search=LICENCIA")).EnumerateArray().ToList();

        items.Select(i => i.GetProperty("code").GetString()).Should().Equal("LIC-A");
    }

    [Fact]
    public async Task AProductWithPrice_LocksTheBaseCurrency()
    {
        var (_, client) = await CreateCompanyWithAdminAsync();
        var usdId = await CurrencyIdAsync("USD");
        (await client.PostAsJsonAsync("/api/v1/Products", new { code = "FREE", name = "Sin precio", kind = 2, referencePrice = 0m })).EnsureSuccessStatusCode();

        var stillFree = await client.PostAsJsonAsync("/api/v1/CompanyCurrencies", new { currencyId = usdId, isBase = false });
        stillFree.EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/v1/Products", new { code = "PAID", name = "Con precio", kind = 1, referencePrice = 10m })).EnsureSuccessStatusCode();
        var usdRow = Data(await client.GetStringAsync("/api/v1/CompanyCurrencies")).EnumerateArray().Single(c => c.GetProperty("code").GetString() == "USD");

        var change = await client.PutAsJsonAsync($"/api/v1/CompanyCurrencies/{usdRow.GetProperty("id").GetGuid()}", new { isBase = true, isActive = true });

        change.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await change.Content.ReadAsStringAsync()).Should().Contain("BASE_CURRENCY_IN_USE").And.Contain("Products");
    }

    [Fact]
    public async Task Categories_RejectCycles_AndCannotBeDeletedWhileInUse()
    {
        var (_, client) = await CreateCompanyWithAdminAsync();
        var parent = Data(await (await client.PostAsJsonAsync("/api/v1/ProductCategories", new { name = "Software" })).Content.ReadAsStringAsync()).GetProperty("id").GetGuid();
        var child = Data(await (await client.PostAsJsonAsync("/api/v1/ProductCategories", new { name = "Licencias", parentId = parent })).Content.ReadAsStringAsync()).GetProperty("id").GetGuid();

        var cycle = await client.PutAsJsonAsync($"/api/v1/ProductCategories/{parent}", new { name = "Software", parentId = child });
        cycle.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await cycle.Content.ReadAsStringAsync()).Should().Contain("PRODUCT_CATEGORY_CYCLE");

        (await client.PostAsJsonAsync("/api/v1/ProductCategories", new { name = "software" })).StatusCode.Should().Be(HttpStatusCode.Conflict, "two root categories with the same name collide");

        var inUse = await client.DeleteAsync($"/api/v1/ProductCategories/{parent}");
        inUse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await inUse.Content.ReadAsStringAsync()).Should().Contain("Active ProductCategories: 1");
    }

    [Fact]
    public async Task DuplicateRootCategoryNames_AreRejectedByTheDatabaseToo()
    {
        var (companyId, _) = await CreateCompanyWithAdminAsync();
        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        const string insert = """
            INSERT INTO catalog.productcategories (id, companyid, name, parentid, sortorder, isactive, gcrecord, created)
            VALUES (gen_random_uuid(), @company, 'Root', NULL, 0, TRUE, 0, NOW())
            """;
        await using (var first = new NpgsqlCommand(insert, conn))
        {
            first.Parameters.AddWithValue("company", companyId);
            await first.ExecuteNonQueryAsync();
        }

        await using var second = new NpgsqlCommand(insert, conn);
        second.Parameters.AddWithValue("company", companyId);
        var act = () => second.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23505");
    }

    [Fact]
    public async Task NewSql_ShouldPlanAgainstTheMigratedSchema()
    {
        var (companyId, _) = await CreateCompanyWithAdminAsync();
        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            EXPLAIN (FORMAT JSON)
            SELECT p.Id, p.Code, p.Name, pc.Name, COALESCE(bc.Code, '')
            FROM catalog.products p
            LEFT JOIN catalog.productcategories pc ON pc.Id = p.ProductCategoryId
            LEFT JOIN finance.companycurrencies bcc ON bcc.CompanyId = p.CompanyId AND bcc.IsBase = TRUE AND bcc.GcRecord = 0
            LEFT JOIN finance.currencies bc ON bc.Id = bcc.CurrencyId
            WHERE p.CompanyId = @company AND p.GcRecord = 0 AND (p.Code ILIKE @search OR p.Name ILIKE @search)
            ORDER BY p.Name, p.Code LIMIT 20
            """, conn);
        cmd.Parameters.AddWithValue("company", companyId);
        cmd.Parameters.AddWithValue("search", "%lic%");

        ((string?)await cmd.ExecuteScalarAsync()).Should().Contain("products");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> CurrencyIdAsync(string code)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Currencies.IgnoreQueryFilters().Where(c => c.Code == code && c.GcRecord == 0).Select(c => c.Id).SingleAsync();
    }

    /// <summary>Creates a company with <c>POST /Companies</c> (DOP base by seed) and a fresh SuperAdmin bound to it.</summary>
    private async Task<(Guid CompanyId, HttpClient Client)> CreateCompanyWithAdminAsync()
    {
        using var superAdmin = await ModuleTestHelpers.LoginAsync(_factory, "lcano@join.com");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var created = await superAdmin.PostAsJsonAsync("/api/v1/Companies", new { name = $"CAT-{suffix}", taxId = $"CAT{suffix}", isActive = true });
        var body = await created.Content.ReadAsStringAsync();
        created.IsSuccessStatusCode.Should().BeTrue(body);
        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data");
        var companyId = data.ValueKind == JsonValueKind.String ? data.GetGuid() : data.GetProperty("id").GetGuid();

        var email = $"cat-{suffix}@t.local";
        using var anonymous = _factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/users/register", new RegisterCommand { Email = email, Password = Password, FirstName = "Cat", LastName = "Admin" }))
            .EnsureSuccessStatusCode();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            db.UserCompanies.Add(new UserCompany { UserId = user.Id, CompanyId = companyId, IsDefault = true, CreatedBy = "spec57-tests" });
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
