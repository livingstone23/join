// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JOIN.IntegrationTests.Security;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static JOIN.IntegrationTests.Customers.CommercialTestHelpers;

namespace JOIN.IntegrationTests.Customers;

/// <summary>
/// SPEC 64 — commercial reports against PostgreSQL 17 (criteria 1–6 and the <c>EXPLAIN</c> of the report queries).
/// </summary>
public sealed class LeadReportsPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public LeadReportsPostgreSqlTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string Period
    {
        get
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return $"from={today.AddDays(-30):yyyy-MM-dd}&to={today:yyyy-MM-dd}";
        }
    }

    private sealed record Company(Guid Id, HttpClient Admin, JsonElement Catalogs, Dictionary<string, Guid> Roles)
    {
        public Guid Id_(string list, string code) =>
            Catalogs.GetProperty(list).EnumerateArray().Single(i => i.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();
    }

    private async Task<Company> CompanyAsync()
    {
        var (id, admin, _) = await CreateCompanyWithUserAsync(_factory);
        (await admin.PutAsJsonAsync("/api/v1/LeadCompanySettings", new { duplicateOpenLeadPolicy = 3, requireIdentificationForCustomer = false, codePrefix = "LD-", codeSequenceLength = 6 })).EnsureSuccessStatusCode();
        await GrantAsync(_factory, id, "UsuarioSimple", "Leads");
        await GrantAsync(_factory, id, "UsuarioSimple", "LeadReports", create: false, update: false, delete: false);
        var catalogs = await DataAsync(await admin.GetAsync("/api/v1/Leads/catalogs"));
        var roles = (await DataAsync(await admin.GetAsync("/api/v1/TeamRoles"))).EnumerateArray().ToDictionary(r => r.GetProperty("code").GetString()!, r => r.GetProperty("id").GetGuid());
        return new Company(id, admin, catalogs, roles);
    }

    private static async Task<Guid> TeamAsync(Company c, string name) =>
        (await DataAsync(await c.Admin.PostAsJsonAsync("/api/v1/SalesTeams", new { name }))).GetProperty("id").GetGuid();

    private static async Task MemberAsync(Company c, Guid team, Guid userId, string role, bool primary = false) =>
        (await c.Admin.PostAsJsonAsync($"/api/v1/SalesTeams/{team}/members", new { userId, teamRoleId = c.Roles[role], isPrimary = primary })).EnsureSuccessStatusCode();

    private static async Task<Guid> LeadAsync(Company c, HttpClient client, Guid? team = null, object? currencyId = null, decimal value = 1000m)
    {
        var response = await client.PostAsJsonAsync("/api/v1/Leads", new
        {
            newPerson = new { personType = 2, firstName = "Rep", lastName = "SRL", commercialName = $"Rep {Guid.NewGuid():N}"[..20] },
            topic = "Reporte", relationshipTypeId = c.Id_("relationshipTypes", "PROSPECT_CUSTOMER"), leadSourceId = c.Id_("sources", "WEB"),
            salesTeamId = team, currencyId, estimatedValue = value
        });
        return (await DataAsync(response)).GetProperty("lead").GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> WinAsync(Company c, HttpClient client, Guid leadId) =>
        await DataAsync(await client.PostAsJsonAsync($"/api/v1/Leads/{leadId}/win", new { leadReasonId = c.Id_("reasons", "BEST_PRICE") }));

    [Fact]
    public async Task DevSeed_SummaryMatchesByTeam_AndRanking()
    {
        using var superAdmin = await ModuleTestHelpers.LoginAsync(_factory, "lcano@join.com");

        var summary = await DataAsync(await superAdmin.GetAsync($"/api/v1/LeadReports/summary?{Period}"));
        var teams = (await DataAsync(await superAdmin.GetAsync($"/api/v1/LeadReports/by-team?{Period}"))).EnumerateArray().ToList();
        var ranking = (await DataAsync(await superAdmin.GetAsync($"/api/v1/LeadReports/ranking?{Period}"))).EnumerateArray().ToList();

        summary.GetProperty("wonCount").GetInt32().Should().BeGreaterThan(0, "the dev seed has a won lead");
        teams.Sum(t => t.GetProperty("createdCount").GetInt32()).Should().Be(summary.GetProperty("createdCount").GetInt32());
        teams.Sum(t => t.GetProperty("wonCount").GetInt32()).Should().Be(summary.GetProperty("wonCount").GetInt32());
        teams.Sum(t => t.GetProperty("wonValueBase").GetDecimal()).Should().Be(summary.GetProperty("wonValueBase").GetDecimal());
        ranking.Sum(r => r.GetProperty("createdCount").GetInt32()).Should().Be(summary.GetProperty("createdCount").GetInt32());
        ranking.Sum(r => r.GetProperty("wonCount").GetInt32()).Should().Be(summary.GetProperty("wonCount").GetInt32());
        summary.GetProperty("baseCurrencyCode").GetString().Should().Be("DOP");

        var funnel = (await DataAsync(await superAdmin.GetAsync($"/api/v1/LeadReports/funnel?{Period}"))).EnumerateArray().ToList();
        funnel.Sum(f => f.GetProperty("count").GetInt32()).Should().Be(summary.GetProperty("openCount").GetInt32());
        (await DataAsync(await superAdmin.GetAsync($"/api/v1/LeadReports/by-product?{Period}"))).EnumerateArray()
            .Should().Contain(p => p.GetProperty("productCode").GetString() == "CRM-USR");
        (await DataAsync(await superAdmin.GetAsync($"/api/v1/LeadReports/by-contact-type?{Period}"))).GetProperty("wonLeadsCount").GetInt32()
            .Should().Be(summary.GetProperty("wonCount").GetInt32());
        (await DataAsync(await superAdmin.GetAsync($"/api/v1/LeadReports/by-source?{Period}"))).GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AUserInTwoTeams_CountsTheWinOnlyForTheLeadsTeam_AndAReactivatedLeadForTheWinner()
    {
        var c = await CompanyAsync();
        var a = await TeamAsync(c, "A");
        var b = await TeamAsync(c, "B");
        var (seller, sellerId) = await AddUserAsync(_factory, c.Id, "UsuarioSimple");
        var (other, otherId) = await AddUserAsync(_factory, c.Id, "UsuarioSimple");
        await MemberAsync(c, a, sellerId, "MEMBER", primary: true);
        await MemberAsync(c, b, sellerId, "MEMBER");
        await MemberAsync(c, b, otherId, "MEMBER", primary: true);

        await WinAsync(c, seller, await LeadAsync(c, seller, a));

        var reactivated = await LeadAsync(c, seller, a);
        (await seller.PostAsJsonAsync($"/api/v1/Leads/{reactivated}/inactivate", new { leadReasonId = c.Id_("reasons", "POSTPONED") })).EnsureSuccessStatusCode();
        (await other.PostAsync($"/api/v1/Leads/{reactivated}/reactivate", null)).EnsureSuccessStatusCode();
        await WinAsync(c, other, reactivated);

        var teams = (await DataAsync(await c.Admin.GetAsync($"/api/v1/LeadReports/by-team?{Period}"))).EnumerateArray()
            .ToDictionary(t => t.GetProperty("salesTeamName").GetString()!, t => t.GetProperty("wonCount").GetInt32());
        teams["A"].Should().Be(1, "only the first lead was won for team A");
        teams["B"].Should().Be(1, "the reactivated lead took the winner's team");
        var ranking = (await DataAsync(await c.Admin.GetAsync($"/api/v1/LeadReports/ranking?{Period}"))).EnumerateArray()
            .ToDictionary(r => r.GetProperty("userId").GetGuid(), r => r.GetProperty("wonCount").GetInt32());
        ranking[sellerId].Should().Be(1);
        ranking[otherId].Should().Be(1);
    }

    [Fact]
    public async Task AMember_SeesOnlyOwnFigures_EvenAskingForItsTeam_AndACompanySupervisorSeesEverything()
    {
        var c = await CompanyAsync();
        var team = await TeamAsync(c, "Capital");
        var (member, memberId) = await AddUserAsync(_factory, c.Id, "UsuarioSimple");
        var (colleague, colleagueId) = await AddUserAsync(_factory, c.Id, "UsuarioSimple");
        var (supervisor, supervisorId) = await AddUserAsync(_factory, c.Id, "UsuarioSimple");
        await MemberAsync(c, team, memberId, "MEMBER", primary: true);
        await MemberAsync(c, team, colleagueId, "MEMBER", primary: true);
        await MemberAsync(c, team, supervisorId, "SUPERVISOR", primary: true);
        await WinAsync(c, member, await LeadAsync(c, member, team));
        await WinAsync(c, colleague, await LeadAsync(c, colleague, team));
        await LeadAsync(c, c.Admin);

        var own = await DataAsync(await member.GetAsync($"/api/v1/LeadReports/summary?{Period}&salesTeamId={team}"));
        own.GetProperty("wonCount").GetInt32().Should().Be(1);
        own.GetProperty("createdCount").GetInt32().Should().Be(1);

        var all = await DataAsync(await supervisor.GetAsync($"/api/v1/LeadReports/summary?{Period}"));
        all.GetProperty("wonCount").GetInt32().Should().Be(2);
        all.GetProperty("createdCount").GetInt32().Should().Be(3);
        (await DataAsync(await supervisor.GetAsync($"/api/v1/LeadReports/by-team?{Period}"))).EnumerateArray()
            .Select(t => t.GetProperty("salesTeamName").GetString()).Should().BeEquivalentTo("Capital", "Sin grupo");

        var (_, foreign, _) = await CreateCompanyWithUserAsync(_factory);
        var invalidTeam = await foreign.GetAsync($"/api/v1/LeadReports/summary?{Period}&salesTeamId={team}");
        invalidTeam.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalidTeam.Content.ReadAsStringAsync()).Should().Contain("INVALID_SALES_TEAM");
        var invalidPeriod = await member.GetAsync("/api/v1/LeadReports/summary?from=2026-10-10&to=2026-10-01");
        (await invalidPeriod.Content.ReadAsStringAsync()).Should().Contain("REPORT_PERIOD_INVALID");
    }

    [Fact]
    public async Task UsdLead_SumsItsStoredBaseValue_NotARecalculation()
    {
        var c = await CompanyAsync();
        Guid usd;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            usd = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Currencies.IgnoreQueryFilters().Where(x => x.Code == "USD").Select(x => x.Id).SingleAsync();
        }

        (await c.Admin.PostAsJsonAsync("/api/v1/CompanyCurrencies", new { currencyId = usd })).EnsureSuccessStatusCode();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        (await c.Admin.PostAsJsonAsync("/api/v1/ExchangeRates", new { currencyId = usd, rateDate = today.AddDays(-1).ToString("yyyy-MM-dd"), rate = 60m })).EnsureSuccessStatusCode();
        var won = (await WinAsync(c, c.Admin, await LeadAsync(c, c.Admin, currencyId: usd, value: 100m))).GetProperty("lead");
        won.GetProperty("wonValueBase").GetDecimal().Should().Be(6000m);
        (await c.Admin.PostAsJsonAsync("/api/v1/ExchangeRates", new { currencyId = usd, rateDate = today.ToString("yyyy-MM-dd"), rate = 75m })).EnsureSuccessStatusCode();

        var summary = await DataAsync(await c.Admin.GetAsync($"/api/v1/LeadReports/summary?{Period}"));
        summary.GetProperty("wonValueBase").GetDecimal().Should().Be(6000m);
        (await DataAsync(await c.Admin.GetAsync($"/api/v1/LeadReports/won-leads?{Period}"))).GetProperty("items").EnumerateArray().Single()
            .GetProperty("wonValueBase").GetDecimal().Should().Be(6000m);
    }

    [Fact]
    public async Task Export_WithoutCanExport_Is403()
    {
        var c = await CompanyAsync();
        var (member, _) = await AddUserAsync(_factory, c.Id, "UsuarioSimple");

        (await member.GetAsync($"/api/v1/LeadReports/won-leads?{Period}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await member.GetAsync($"/api/v1/LeadReports/won-leads?{Period}&export=true")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var exported = await DataAsync(await c.Admin.GetAsync($"/api/v1/LeadReports/won-leads?{Period}&export=true"));
        exported.GetProperty("pageSize").GetInt32().Should().Be(10_000);

        using var agent = await ModuleTestHelpers.LoginAsync(_factory, "agent@join.com");
        (await agent.GetAsync($"/api/v1/LeadReports/summary?{Period}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ReportSql_ShouldPlanAgainstTheMigratedSchema()
    {
        var c = await CompanyAsync();
        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        const string where = "l.CompanyId = @company AND l.GcRecord = 0 AND (l.OwnerUserId = @user OR l.SalesTeamId = ANY(@teams))";
        const string won = "l.Status = 3 AND l.ClosedAt >= @from AND l.ClosedAt < @to";
        const string created = "l.Created >= @from AND l.Created < @to";
        string[] queries =
        [
            $"SELECT COUNT(*) FILTER (WHERE {created}), COUNT(*) FILTER (WHERE {won}), SUM(l.WonValueBase) FILTER (WHERE {won}) FROM customer.leads l WHERE {where}",
            $"SELECT s.Id, COUNT(l.Id), COALESCE(SUM(l.EstimatedValueBase), 0) FROM customer.leadstages s LEFT JOIN customer.leads l ON l.LeadStageId = s.Id AND l.Status = 1 AND {where} WHERE s.CompanyId = @company AND s.GcRecord = 0 AND s.Kind IN (1, 2) GROUP BY s.Id",
            $"SELECT l.SalesTeamId, COUNT(*) FILTER (WHERE {won}) FROM customer.leads l LEFT JOIN customer.salesteams t ON t.Id = l.SalesTeamId WHERE {where} AND (({created}) OR ({won})) GROUP BY l.SalesTeamId, t.Name",
            $"WITH period AS (SELECT l.Id, l.OwnerUserId FROM customer.leads l WHERE {where} AND {won}) SELECT p.OwnerUserId, (SELECT pr.Name FROM customer.leadproducts lp INNER JOIN catalog.products pr ON pr.Id = lp.ProductId WHERE lp.LeadId = p.Id AND lp.Status = 2 GROUP BY pr.Name ORDER BY COUNT(*) DESC LIMIT 1) FROM period p",
            $"SELECT pr.Id, COUNT(DISTINCT l.Id), SUM(ROUND(lp.LineTotal * COALESCE(l.WonExchangeRate, l.ExchangeRate), 2)) FROM customer.leads l INNER JOIN customer.leadproducts lp ON lp.LeadId = l.Id AND lp.Status = 2 INNER JOIN catalog.products pr ON pr.Id = lp.ProductId WHERE {where} AND {won} GROUP BY pr.Id",
            $"SELECT src.Id, COUNT(*) FILTER (WHERE {created}) FROM customer.leads l INNER JOIN customer.leadsources src ON src.Id = l.LeadSourceId WHERE {where} GROUP BY src.Id",
            $"SELECT ctc.Name, COUNT(*) FROM customer.leads l INNER JOIN customer.leadinteractions i ON i.LeadId = l.Id INNER JOIN customer.contacttypecompanies ctc ON ctc.Id = i.ContactTypeCompanyId WHERE {where} AND {won} GROUP BY ctc.Name",
            $"SELECT l.Id, l.Code FROM customer.leads l INNER JOIN admin.persons p ON p.Id = l.PersonId WHERE {where} AND {won} ORDER BY l.ClosedAt DESC LIMIT 20"
        ];

        foreach (var sql in queries)
        {
            await using var cmd = new NpgsqlCommand($"EXPLAIN (FORMAT JSON) {sql}", conn);
            cmd.Parameters.AddWithValue("company", c.Id);
            cmd.Parameters.AddWithValue("user", Guid.NewGuid());
            cmd.Parameters.AddWithValue("teams", new[] { Guid.NewGuid() });
            cmd.Parameters.AddWithValue("from", DateTime.UtcNow.Date.AddDays(-30));
            cmd.Parameters.AddWithValue("to", DateTime.UtcNow.Date.AddDays(1));
            ((string?)await cmd.ExecuteScalarAsync()).Should().Contain("Plan", sql);
        }
    }
}
