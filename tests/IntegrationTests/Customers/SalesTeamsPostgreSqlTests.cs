// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JOIN.Application.Interface.Customers;
using JOIN.Domain.Security;
using JOIN.IntegrationTests.Security;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static JOIN.IntegrationTests.Customers.CommercialTestHelpers;

namespace JOIN.IntegrationTests.Customers;

/// <summary>
/// SPEC 60 — sales teams and management roles against PostgreSQL 17: primary membership, one leader per team, lead
/// scope resolution, history kept on leave, <c>/SalesTeams/me</c>, closing memberships when the user leaves the
/// company, and <c>EXPLAIN</c> of the new SQL.
/// </summary>
public sealed class SalesTeamsPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SalesTeamsPostgreSqlTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static async Task<Dictionary<string, Guid>> RolesAsync(HttpClient client) =>
        (await DataAsync(await client.GetAsync("/api/v1/TeamRoles"))).EnumerateArray()
            .ToDictionary(r => r.GetProperty("code").GetString()!, r => r.GetProperty("id").GetGuid());

    private static async Task<Guid> TeamAsync(HttpClient client, string name) =>
        (await DataAsync(await client.PostAsJsonAsync("/api/v1/SalesTeams", new { name }))).GetProperty("id").GetGuid();

    private static async Task<JsonElement> AddAsync(HttpClient client, Guid teamId, Guid userId, Guid roleId, bool isPrimary = false, string? validFrom = null)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/SalesTeams/{teamId}/members", new { userId, teamRoleId = roleId, isPrimary, validFrom });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await DataAsync(response);
    }

    private async Task<LeadAccessScope> ScopeAsync(Guid companyId, Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ILeadAccessScopeResolver>().ResolveAsync(companyId, userId);
    }

    [Fact]
    public async Task EveryCompany_GetsTheFourRoles()
    {
        var (_, client, _) = await CreateCompanyWithUserAsync(_factory);

        var roles = (await DataAsync(await client.GetAsync("/api/v1/TeamRoles"))).EnumerateArray().ToList();

        roles.Select(r => r.GetProperty("code").GetString()).Should().Equal("SUPERVISOR", "LEADER", "COORDINATOR", "MEMBER");
        roles.First().GetProperty("name").GetString().Should().Be("Supervisor comercial");
        roles.First().GetProperty("leadScope").GetInt32().Should().Be(3);
        roles.Single(r => r.GetProperty("code").GetString() == "LEADER").GetProperty("isLeader").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task PrimaryMembership_MovesInOneTransaction_AndPassesToTheOldestWhenClosed()
    {
        var (companyId, client, _) = await CreateCompanyWithUserAsync(_factory);
        var (_, userId) = await AddUserAsync(_factory, companyId, "UsuarioSimple");
        var roles = await RolesAsync(client);
        var a = await TeamAsync(client, "A");
        var b = await TeamAsync(client, "B");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var inA = await AddAsync(client, a, userId, roles["SUPERVISOR"], validFrom: today.AddDays(-10).ToString("yyyy-MM-dd"));
        var inB = await AddAsync(client, b, userId, roles["SUPERVISOR"]);
        inA.GetProperty("isPrimary").GetBoolean().Should().BeTrue();
        inB.GetProperty("isPrimary").GetBoolean().Should().BeFalse();

        var mark = await client.PutAsJsonAsync($"/api/v1/SalesTeams/{b}/members/{inB.GetProperty("id").GetGuid()}", new { teamRoleId = roles["SUPERVISOR"], isPrimary = true });
        (await DataAsync(mark)).GetProperty("isPrimary").GetBoolean().Should().BeTrue();
        (await PrimaryOfAsync(userId)).Should().Equal(inB.GetProperty("id").GetGuid());

        var close = await client.PostAsync($"/api/v1/SalesTeams/{b}/members/{inB.GetProperty("id").GetGuid()}/close", null);
        (await DataAsync(close)).GetProperty("validTo").GetString().Should().Be(today.ToString("yyyy-MM-dd"));
        (await PrimaryOfAsync(userId)).Should().Equal(inA.GetProperty("id").GetGuid());

        (await client.PostAsJsonAsync($"/api/v1/SalesTeams/{a}/members", new { userId, teamRoleId = roles["MEMBER"] })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var invalid = await client.PostAsJsonAsync($"/api/v1/SalesTeams/{a}/members/{inA.GetProperty("id").GetGuid()}/close", new { validTo = today.AddDays(-30).ToString("yyyy-MM-dd") });
        (await invalid.Content.ReadAsStringAsync()).Should().Contain("SALES_TEAM_MEMBER_DATES_INVALID");
        await client.PostAsync($"/api/v1/SalesTeams/{a}/members/{inA.GetProperty("id").GetGuid()}/close", null);
        (await PrimaryOfAsync(userId)).Should().BeEmpty("no open membership left: the user has no team");

        var detail = await DataAsync(await client.GetAsync($"/api/v1/SalesTeams/{b}"));
        var history = detail.GetProperty("members").EnumerateArray().Single();
        history.GetProperty("validTo").GetString().Should().NotBeNull("a leave never deletes rows");
        detail.GetProperty("currentMembersCount").GetInt32().Should().Be(1, "closed today is still current today");
    }

    [Fact]
    public async Task ATeam_HasOneLeader()
    {
        var (companyId, client, _) = await CreateCompanyWithUserAsync(_factory);
        var (_, first) = await AddUserAsync(_factory, companyId, "UsuarioSimple");
        var (_, second) = await AddUserAsync(_factory, companyId, "UsuarioSimple");
        var roles = await RolesAsync(client);
        var team = await TeamAsync(client, "Capital");
        await AddAsync(client, team, first, roles["LEADER"]);

        await AddAsync(client, team, second, roles["LEADER"]);

        var members = (await DataAsync(await client.GetAsync($"/api/v1/SalesTeams/{team}/members?currentOnly=true"))).EnumerateArray().ToList();
        members.Single(m => m.GetProperty("userId").GetGuid() == first).GetProperty("teamRoleCode").GetString().Should().Be("MEMBER");
        members.Single(m => m.GetProperty("userId").GetGuid() == second).GetProperty("teamRoleCode").GetString().Should().Be("LEADER");
        (await DataAsync(await client.GetAsync("/api/v1/SalesTeams"))).EnumerateArray().Single().GetProperty("leaderUserId").GetGuid().Should().Be(second);
    }

    [Fact]
    public async Task Resolver_FollowsTheRoleScopes_AndAScopeChangeAppliesOnTheNextCall()
    {
        var (companyId, client, _) = await CreateCompanyWithUserAsync(_factory);
        var roles = await RolesAsync(client);
        var a = await TeamAsync(client, "A");
        var b = await TeamAsync(client, "B");
        var (_, member) = await AddUserAsync(_factory, companyId, "UsuarioSimple");
        var (_, leader) = await AddUserAsync(_factory, companyId, "UsuarioSimple");
        var (_, supervisor) = await AddUserAsync(_factory, companyId, "UsuarioSimple");
        var (_, mixed) = await AddUserAsync(_factory, companyId, "UsuarioSimple");
        var (_, alone) = await AddUserAsync(_factory, companyId, "UsuarioSimple");
        await AddAsync(client, a, member, roles["MEMBER"]);
        await AddAsync(client, a, leader, roles["LEADER"]);
        await AddAsync(client, a, supervisor, roles["SUPERVISOR"]);
        await AddAsync(client, b, supervisor, roles["SUPERVISOR"]);
        await AddAsync(client, a, mixed, roles["MEMBER"]);
        await AddAsync(client, b, mixed, roles["COORDINATOR"]);

        var memberScope = await ScopeAsync(companyId, member);
        memberScope.IsCompanyWide.Should().BeFalse();
        memberScope.TeamIds.Should().BeEmpty();
        (await ScopeAsync(companyId, leader)).TeamIds.Should().Equal(a);
        (await ScopeAsync(companyId, supervisor)).IsCompanyWide.Should().BeTrue();
        var mixedScope = await ScopeAsync(companyId, mixed);
        mixedScope.TeamIds.Should().Equal(b);
        mixedScope.Covers(mixed, a).Should().BeTrue("own leads");
        mixedScope.Covers(member, a).Should().BeFalse();
        var aloneScope = await ScopeAsync(companyId, alone);
        aloneScope.TeamIds.Should().BeEmpty();
        aloneScope.IsCompanyWide.Should().BeFalse();

        var change = await client.PutAsJsonAsync($"/api/v1/TeamRoles/{roles["MEMBER"]}", new { code = "MEMBER", name = "Integrante", leadScope = 2, sortOrder = 4 });
        change.EnsureSuccessStatusCode();
        (await ScopeAsync(companyId, member)).TeamIds.Should().Equal(a);

        var inUse = await client.DeleteAsync($"/api/v1/TeamRoles/{roles["MEMBER"]}");
        inUse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var teamInUse = await client.DeleteAsync($"/api/v1/SalesTeams/{a}");
        teamInUse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await teamInUse.Content.ReadAsStringAsync()).Should().Contain("SALES_TEAM_IN_USE");
    }

    [Fact]
    public async Task Me_ReturnsOnlyTheCurrentMembershipsOfTheTokenUser()
    {
        var (companyId, client, adminId) = await CreateCompanyWithUserAsync(_factory);
        var (_, otherUser) = await AddUserAsync(_factory, companyId, "UsuarioSimple");
        var roles = await RolesAsync(client);
        var a = await TeamAsync(client, "A");
        var b = await TeamAsync(client, "B");
        var c = await TeamAsync(client, "C");
        await AddAsync(client, a, adminId, roles["LEADER"]);
        var inB = await AddAsync(client, b, adminId, roles["MEMBER"], validFrom: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5).ToString("yyyy-MM-dd"));
        await AddAsync(client, c, otherUser, roles["MEMBER"]);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.SalesTeamMembers.IgnoreQueryFilters().SingleAsync(m => m.Id == inB.GetProperty("id").GetGuid());
            row.ValidTo = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
            await db.SaveChangesAsync();
        }

        var mine = (await DataAsync(await client.GetAsync("/api/v1/SalesTeams/me"))).EnumerateArray().ToList();

        mine.Select(m => m.GetProperty("salesTeamId").GetGuid()).Should().Equal(a);
        mine.Single().GetProperty("isPrimary").GetBoolean().Should().BeTrue();
        mine.Single().GetProperty("leadScope").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task LeavingTheCompany_ClosesTheUsersMemberships()
    {
        var (home, _, _) = await CreateCompanyWithUserAsync(_factory);
        var (other, otherClient, _) = await CreateCompanyWithUserAsync(_factory);
        var (_, userId) = await AddUserAsync(_factory, home, "UsuarioSimple");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.UserCompanies.Add(new UserCompany { UserId = userId, CompanyId = other, IsDefault = false, CreatedBy = "spec60-tests" });
            await db.SaveChangesAsync();
        }

        var roles = await RolesAsync(otherClient);
        var team = await TeamAsync(otherClient, "Norte");
        await AddAsync(otherClient, team, userId, roles["MEMBER"]);

        using var superAdmin = await ModuleTestHelpers.LoginAsync(_factory, "lcano@join.com");
        var removed = await superAdmin.DeleteAsync($"/api/v1/Users/{userId}/companies/{other}");
        removed.IsSuccessStatusCode.Should().BeTrue(await removed.Content.ReadAsStringAsync());

        await using var check = _factory.Services.CreateAsyncScope();
        var rows = await check.ServiceProvider.GetRequiredService<ApplicationDbContext>().SalesTeamMembers.IgnoreQueryFilters()
            .Where(m => m.UserId == userId).ToListAsync();
        rows.Should().ContainSingle().Which.ValidTo.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
        rows.Single().IsPrimary.Should().BeFalse();
    }

    [Fact]
    public async Task Permissions_SupervisorReads_SimpleUserIsForbidden()
    {
        using var supervisor = await ModuleTestHelpers.LoginAsync(_factory, "supervisor@join.com");
        (await supervisor.GetAsync("/api/v1/SalesTeams")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await supervisor.PostAsJsonAsync("/api/v1/SalesTeams", new { name = "Nope" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var simple = await ModuleTestHelpers.LoginAsync(_factory, "simpleuser@join.com");
        (await simple.GetAsync("/api/v1/SalesTeams")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await simple.GetAsync("/api/v1/TeamRoles")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var superAdmin = await ModuleTestHelpers.LoginAsync(_factory, "lcano@join.com");
        var joinTeams = (await DataAsync(await superAdmin.GetAsync("/api/v1/SalesTeams"))).EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        joinTeams.Should().Contain(new[] { "Equipo Capital", "Equipo Norte", "Cuentas Corporativas" }, "development seed of JOIN-001");
    }

    [Fact]
    public async Task NewSql_ShouldPlanAgainstTheMigratedSchema()
    {
        var (companyId, _, userId) = await CreateCompanyWithUserAsync(_factory);
        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        string[] queries =
        [
            """
            SELECT m.SalesTeamId, r.LeadScope, r.CanReassign FROM customer.salesteammembers m
            INNER JOIN customer.teamroles r ON r.Id = m.TeamRoleId
            INNER JOIN customer.salesteams t ON t.Id = m.SalesTeamId AND t.GcRecord = 0
            WHERE m.CompanyId = @company AND m.UserId = @user AND m.GcRecord = 0 AND m.ValidFrom <= CURRENT_DATE AND (m.ValidTo IS NULL OR m.ValidTo >= CURRENT_DATE)
            """,
            """
            SELECT t.Id, (SELECT COUNT(*) FROM customer.salesteammembers m WHERE m.SalesTeamId = t.Id AND m.GcRecord = 0) AS c, leader.UserId
            FROM customer.salesteams t
            LEFT JOIN LATERAL (SELECT m.UserId FROM customer.salesteammembers m INNER JOIN customer.teamroles r ON r.Id = m.TeamRoleId AND r.IsLeader = TRUE
                               WHERE m.SalesTeamId = t.Id AND m.GcRecord = 0 ORDER BY m.ValidFrom LIMIT 1) leader ON TRUE
            WHERE t.CompanyId = @company AND t.GcRecord = 0 ORDER BY t.Name
            """
        ];

        foreach (var sql in queries)
        {
            await using var cmd = new NpgsqlCommand($"EXPLAIN (FORMAT JSON) {sql}", conn);
            cmd.Parameters.AddWithValue("company", companyId);
            cmd.Parameters.AddWithValue("user", userId);
            ((string?)await cmd.ExecuteScalarAsync()).Should().Contain("Plan");
        }
    }

    private async Task<List<Guid>> PrimaryOfAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SalesTeamMembers.IgnoreQueryFilters()
            .Where(m => m.UserId == userId && m.IsPrimary && m.ValidTo == null).Select(m => m.Id).ToListAsync();
    }
}
