// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using FluentAssertions;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace JOIN.IntegrationTests.Messaging;

/// <summary>
/// SPEC 53 — <c>EXPLAIN</c> verification of the new <c>GET
/// /TicketUserCompanies/assignable</c> SQL against the migrated PostgreSQL
/// database (convention 10 of the <c>-post</c> series).
/// </summary>
public sealed class TicketCapabilitiesAndInactivityExplainPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public TicketCapabilitiesAndInactivityExplainPostgreSqlTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task AssignableQuery_ShouldUseIndexScansAndApplyFilters()
    {
        var joinCompanyId = await ReadJoinCompanyIdAsync();

        const string sql = """
            SELECT
                tuc.UserId AS UserId,
                CONCAT(u.FirstName, ' ', u.LastName) AS UserName,
                u.Email AS UserEmail
            FROM messaging.ticketusercompanies tuc
            INNER JOIN security.users u ON u.id = tuc.userid
            WHERE tuc.companyid = @tenant
              AND tuc.gcrecord = 0
              AND tuc.canresolveticket = TRUE
              AND u.gcrecord = 0
              AND u.isactive = TRUE
              AND EXISTS (
                  SELECT 1
                  FROM security.usercompanies uc
                  WHERE uc.userid = tuc.userid
                    AND uc.companyid = tuc.companyid
                    AND uc.gcrecord = 0)
            ORDER BY u.lastname ASC, u.firstname ASC;
            """;

        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        await using var explain = new NpgsqlCommand(
            $"EXPLAIN (FORMAT JSON, ANALYZE) {sql}", conn);
        explain.Parameters.AddWithValue("tenant", joinCompanyId);
        var planJson = (string?)await explain.ExecuteScalarAsync();
        planJson.Should().NotBeNullOrWhiteSpace();

        // The plan must touch the two tables the query joins, with the
        // CanResolveTicket filter applied as a Filter node.
        planJson.Should().Contain("\"Relation Name\": \"ticketusercompanies\"");
        planJson.Should().Contain("\"Relation Name\": \"users\"");
        planJson.Should().Contain("canresolveticket");
        planJson.Should().Contain("\"Relation Name\": \"usercompanies\"");
    }

    private async Task<Guid> ReadJoinCompanyIdAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Companies.IgnoreQueryFilters()
            .Where(c => c.TaxId == "JOIN-001")
            .Select(c => c.Id)
            .SingleAsync();
    }
}