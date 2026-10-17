// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using FluentAssertions;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace JOIN.IntegrationTests.Finance;

/// <summary>
/// SPEC 56 — convention 10: <c>EXPLAIN</c> of the new SQL against the migrated database.
/// </summary>
public sealed class FinanceExplainPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public FinanceExplainPostgreSqlTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("""
        SELECT r.Rate, r.RateDate FROM finance.exchangerates r
        WHERE r.CompanyId = @company AND r.CurrencyId = @currency AND r.GcRecord = 0 AND r.RateDate <= @date
        ORDER BY r.RateDate DESC LIMIT 1
        """)]
    [InlineData("""
        SELECT cc.Id, c.Code, cc.IsBase, lr.Rate, lr.RateDate
        FROM finance.companycurrencies cc
        INNER JOIN finance.currencies c ON c.Id = cc.CurrencyId
        LEFT JOIN LATERAL (
            SELECT r.Rate, r.RateDate FROM finance.exchangerates r
            WHERE r.CompanyId = cc.CompanyId AND r.CurrencyId = cc.CurrencyId AND r.GcRecord = 0 AND r.RateDate <= @date
            ORDER BY r.RateDate DESC LIMIT 1
        ) lr ON TRUE
        WHERE cc.CompanyId = @company AND cc.GcRecord = 0 AND cc.CurrencyId <> @currency
        ORDER BY cc.IsBase DESC, c.Code
        """)]
    public async Task NewSql_ShouldPlanAgainstTheMigratedSchema(string sql)
    {
        Guid companyId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            companyId = await db.Companies.IgnoreQueryFilters().Where(c => c.TaxId == "JOIN-001").Select(c => c.Id).SingleAsync();
        }

        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"EXPLAIN (FORMAT JSON) {sql}", conn);
        cmd.Parameters.AddWithValue("company", companyId);
        cmd.Parameters.AddWithValue("currency", Guid.NewGuid());
        cmd.Parameters.AddWithValue("date", DateTime.UtcNow.Date);

        var plan = (string?)await cmd.ExecuteScalarAsync();

        plan.Should().NotBeNullOrWhiteSpace();
        plan.Should().Contain("exchangerates");
    }
}
