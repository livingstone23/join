// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.IntegrationTests.Security;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static JOIN.IntegrationTests.Customers.CommercialTestHelpers;

namespace JOIN.IntegrationTests.Customers;

/// <summary>
/// SPEC 59 — commercial settings against PostgreSQL 17: per-company seed, lead stage kinds (also in the database),
/// company rules, master + company contact types, reason filter, permissions and tenant isolation.
/// </summary>
public sealed class LeadSettingsPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public LeadSettingsPostgreSqlTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task EveryCompany_HasTheFullSeed_AndReseedingDoesNotDuplicate()
    {
        var (companyId, client, _) = await CreateCompanyWithUserAsync(_factory);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICompanyCatalogSeeder>().SeedDefaultCatalogsForCompanyAsync(companyId);
        }

        var stages = (await DataAsync(await client.GetAsync("/api/v1/LeadStages"))).EnumerateArray().ToList();
        stages.Select(s => s.GetProperty("code").GetString()).Should().Equal("NEW", "CONTACTED", "QUALIFIED", "PROPOSAL", "NEGOTIATION", "WON", "LOST");
        stages.Select(s => s.GetProperty("kind").GetInt32()).Should().Equal(1, 2, 2, 2, 2, 3, 4);
        (await DataAsync(await client.GetAsync("/api/v1/LeadSources"))).GetArrayLength().Should().Be(8);
        (await DataAsync(await client.GetAsync("/api/v1/LeadReasons"))).GetArrayLength().Should().Be(9);
        (await DataAsync(await client.GetAsync("/api/v1/RelationshipTypes"))).EnumerateArray()
            .Where(r => r.GetProperty("onWin").GetInt32() == 1).Select(r => r.GetProperty("code").GetString())
            .Should().BeEquivalentTo("PROSPECT_CUSTOMER", "DISTRIBUTOR");
        var contactTypes = (await DataAsync(await client.GetAsync("/api/v1/ContactTypeCompanies"))).EnumerateArray().ToList();
        contactTypes.Select(c => c.GetProperty("code").GetString()).Should().Contain(new[] { "CALL", "ONLINE_MEETING", "VISIT", "EMAIL", "SMS", "WHATSAPP", "DEMO" });
        contactTypes.Single(c => c.GetProperty("code").GetString() == "EMAIL").GetProperty("communicationChannelCode").GetString().Should().Be("SENDGRID");
        (await DataAsync(await client.GetAsync("/api/v1/LeadCompanySettings"))).GetProperty("isDefault").GetBoolean().Should().BeFalse();

        await using var check = _factory.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.LeadStages.IgnoreQueryFilters().CountAsync(s => s.CompanyId == companyId)).Should().Be(7);
        (await db.ContactTypeCompanies.IgnoreQueryFilters().CountAsync(c => c.CompanyId == companyId)).Should().Be(contactTypes.Count);
        (await db.LeadCompanySettings.IgnoreQueryFilters().CountAsync(c => c.CompanyId == companyId)).Should().Be(1);
        var join = await db.Companies.IgnoreQueryFilters().Where(c => c.TaxId != null && c.Name.Contains("JOIN")).Select(c => c.Id).FirstAsync();
        (await db.LeadStages.IgnoreQueryFilters().CountAsync(s => s.CompanyId == join && s.GcRecord == 0)).Should().Be(7, "companies existing at startup are seeded too");
    }

    [Fact]
    public async Task LeadStages_KeepOneInitialWonAndLost()
    {
        var (companyId, client, _) = await CreateCompanyWithUserAsync(_factory);
        var stages = (await DataAsync(await client.GetAsync("/api/v1/LeadStages"))).EnumerateArray().ToDictionary(s => s.GetProperty("code").GetString()!, s => s.GetProperty("id").GetGuid());

        (await client.PostAsJsonAsync("/api/v1/LeadStages", new { code = "WON2", name = "Ganado 2", kind = 3, probability = 100 })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var delete = await client.DeleteAsync($"/api/v1/LeadStages/{stages["WON"]}");
        delete.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await delete.Content.ReadAsStringAsync()).Should().Contain("LEAD_STAGE_SYSTEM");
        var deactivateInitial = await client.PutAsJsonAsync($"/api/v1/LeadStages/{stages["NEW"]}", new { code = "NEW", name = "Nuevo", kind = 1, probability = 10, isActive = false });
        (await deactivateInitial.Content.ReadAsStringAsync()).Should().Contain("LEAD_STAGE_KIND_REQUIRED");
        var rename = await client.PutAsJsonAsync($"/api/v1/LeadStages/{stages["WON"]}", new { code = "WON", name = "Cerrado ganado", kind = 3, probability = 100, sortOrder = 6 });
        (await DataAsync(rename)).GetProperty("name").GetString().Should().Be("Cerrado ganado");

        // The filtered unique index blocks a second live Won stage even when the application check is bypassed.
        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO customer.leadstages (id, companyid, code, name, probability, sortorder, kind, isactive, gcrecord, created)
            VALUES (gen_random_uuid(), @company, 'WON_RACE', 'Ganado carrera', 100, 99, 3, TRUE, 0, NOW())
            """, conn);
        insert.Parameters.AddWithValue("company", companyId);
        var act = () => insert.ExecuteNonQueryAsync();
        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23505");
    }

    [Fact]
    public async Task CompanySettings_WithoutRow_ReturnDefaults_AndPutCreatesTheRow()
    {
        var (companyId, client, _) = await CreateCompanyWithUserAsync(_factory);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.LeadCompanySettings.IgnoreQueryFilters().Where(s => s.CompanyId == companyId).ExecuteDeleteAsync();
        }

        var defaults = await DataAsync(await client.GetAsync("/api/v1/LeadCompanySettings"));
        defaults.GetProperty("isDefault").GetBoolean().Should().BeTrue();
        defaults.GetProperty("duplicateOpenLeadPolicy").GetInt32().Should().Be(1);
        defaults.GetProperty("requireIdentificationForCustomer").GetBoolean().Should().BeTrue();
        defaults.GetProperty("codePrefix").GetString().Should().Be("LD-");
        defaults.GetProperty("codeSequenceLength").GetInt32().Should().Be(6);

        var saved = await DataAsync(await client.PutAsJsonAsync("/api/v1/LeadCompanySettings", new { duplicateOpenLeadPolicy = 2, requireIdentificationForCustomer = false, codePrefix = "OP-", codeSequenceLength = 5 }));
        saved.GetProperty("isDefault").GetBoolean().Should().BeFalse();
        var reread = await DataAsync(await client.GetAsync("/api/v1/LeadCompanySettings"));
        reread.GetProperty("codePrefix").GetString().Should().Be("OP-");
        reread.GetProperty("duplicateOpenLeadPolicy").GetInt32().Should().Be(2);
        (await client.PutAsJsonAsync("/api/v1/LeadCompanySettings", new { duplicateOpenLeadPolicy = 9, codePrefix = "", codeSequenceLength = 1 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ANewMasterContactType_ReachesEveryCompanyInactive_AndEachCompanyEditsItsOwnCopy()
    {
        var (_, first, _) = await CreateCompanyWithUserAsync(_factory);
        var (_, second, _) = await CreateCompanyWithUserAsync(_factory);
        var code = $"FAIR{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        var created = await first.PostAsJsonAsync("/api/v1/ContactTypes", new { code, name = "Feria", icon = "store", sortOrder = 50 });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        var mine = (await DataAsync(await first.GetAsync("/api/v1/ContactTypeCompanies"))).EnumerateArray().Single(c => c.GetProperty("code").GetString() == code);
        mine.GetProperty("isActive").GetBoolean().Should().BeFalse();
        var activated = await DataAsync(await first.PutAsJsonAsync($"/api/v1/ContactTypeCompanies/{mine.GetProperty("id").GetGuid()}", new { name = "Feria comercial", sortOrder = 1, isActive = true }));
        activated.GetProperty("name").GetString().Should().Be("Feria comercial");
        activated.GetProperty("masterName").GetString().Should().Be("Feria");

        var theirs = (await DataAsync(await second.GetAsync("/api/v1/ContactTypeCompanies"))).EnumerateArray().Single(c => c.GetProperty("code").GetString() == code);
        theirs.GetProperty("isActive").GetBoolean().Should().BeFalse();
        theirs.GetProperty("name").GetString().Should().Be("Feria");
        (await second.PutAsJsonAsync($"/api/v1/ContactTypeCompanies/{mine.GetProperty("id").GetGuid()}", new { name = "Hack", isActive = true })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var manager = await ModuleTestHelpers.LoginAsync(_factory, "manager@join.com");
        (await manager.PostAsJsonAsync("/api/v1/ContactTypes", new { code = "NOPE", name = "No", icon = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LeadReasons_FilterByUsage()
    {
        var (_, client, _) = await CreateCompanyWithUserAsync(_factory);

        var inactive = (await DataAsync(await client.GetAsync("/api/v1/LeadReasons?usedFor=3"))).EnumerateArray().ToList();

        inactive.Select(r => r.GetProperty("code").GetString()).Should().BeEquivalentTo("POSTPONED", "NO_ANSWER", "NO_CAPACITY");
        inactive.Should().OnlyContain(r => r.GetProperty("usedFor").GetInt32() == 3);
    }

    [Fact]
    public async Task WithoutPermission_Is403_AndCatalogsDoNotCrossCompanies()
    {
        using var simple = await ModuleTestHelpers.LoginAsync(_factory, "simpleuser@join.com");
        (await simple.GetAsync("/api/v1/LeadStages")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await simple.GetAsync("/api/v1/LeadCompanySettings")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var supervisor = await ModuleTestHelpers.LoginAsync(_factory, "supervisor@join.com");
        (await supervisor.GetAsync("/api/v1/LeadStages")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await supervisor.PostAsJsonAsync("/api/v1/LeadSources", new { code = "SUP", name = "Supervisor" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var (_, first, _) = await CreateCompanyWithUserAsync(_factory);
        var (_, second, _) = await CreateCompanyWithUserAsync(_factory);
        var source = await DataAsync(await first.PostAsJsonAsync("/api/v1/LeadSources", new { code = "FAIR", name = "Feria" }));
        var id = source.GetProperty("id").GetGuid();

        (await second.GetAsync($"/api/v1/LeadSources/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await second.DeleteAsync($"/api/v1/LeadSources/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await DataAsync(await second.GetAsync("/api/v1/LeadSources"))).EnumerateArray().Should().NotContain(s => s.GetProperty("code").GetString() == "FAIR");
    }

    [Fact]
    public async Task NewSql_ShouldPlanAgainstTheMigratedSchema()
    {
        var (companyId, _, _) = await CreateCompanyWithUserAsync(_factory);
        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        string[] queries =
        [
            "SELECT t.Id, t.Code, t.Probability, t.Kind FROM customer.leadstages t WHERE t.CompanyId = @company AND t.GcRecord = 0 ORDER BY t.SortOrder, t.Name",
            "SELECT t.Id, t.Code, t.UsedFor FROM customer.leadreasons t WHERE t.CompanyId = @company AND t.GcRecord = 0 AND t.UsedFor = 3 ORDER BY t.SortOrder, t.Name",
            "SELECT t.Id, t.Description FROM customer.leadsources t WHERE t.CompanyId = @company AND t.GcRecord = 0 ORDER BY t.SortOrder, t.Name",
            "SELECT t.Id, t.OnWin FROM customer.relationshiptypes t WHERE t.CompanyId = @company AND t.GcRecord = 0 ORDER BY t.SortOrder, t.Name",
            "SELECT c.Id, t.Code, c.Name FROM customer.contacttypecompanies c INNER JOIN customer.contacttypes t ON t.Id = c.ContactTypeId AND t.GcRecord = 0 WHERE c.CompanyId = @company AND c.GcRecord = 0 ORDER BY c.SortOrder, c.Name",
            "SELECT s.Id, s.CodePrefix FROM customer.leadcompanysettings s WHERE s.CompanyId = @company AND s.GcRecord = 0"
        ];

        foreach (var sql in queries)
        {
            await using var cmd = new NpgsqlCommand($"EXPLAIN (FORMAT JSON) {sql}", conn);
            cmd.Parameters.AddWithValue("company", companyId);
            ((string?)await cmd.ExecuteScalarAsync()).Should().Contain("Plan", sql);
        }
    }
}
