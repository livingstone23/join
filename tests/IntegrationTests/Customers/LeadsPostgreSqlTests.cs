// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JOIN.Domain.Customers.Leads;
using JOIN.Domain.Customers.Teams;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static JOIN.IntegrationTests.Customers.CommercialTestHelpers;

namespace JOIN.IntegrationTests.Customers;

/// <summary>
/// SPEC 61 — leads against PostgreSQL 17 (criteria 1–13 and the <c>EXPLAIN</c> of the inbox, board and timeline).
/// </summary>
public sealed class LeadsPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public LeadsPostgreSqlTests(CustomWebApplicationFactory factory) => _factory = factory;

    private sealed record Company(Guid Id, HttpClient Admin, Guid AdminId, JsonElement Catalogs)
    {
        public Guid Stage(string code) => Find(Catalogs.GetProperty("stages"), code);
        public Guid Source(string code) => Find(Catalogs.GetProperty("sources"), code);
        public Guid Reason(string code) => Find(Catalogs.GetProperty("reasons"), code);
        public Guid Relationship(string code) => Find(Catalogs.GetProperty("relationshipTypes"), code);
        public Guid ContactType(string code) => Find(Catalogs.GetProperty("contactTypes"), code);

        private static Guid Find(JsonElement items, string code) =>
            items.EnumerateArray().Single(i => i.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();
    }

    private async Task<Company> CompanyAsync()
    {
        var (companyId, admin, adminId) = await CreateCompanyWithUserAsync(_factory);
        var catalogs = await DataAsync(await admin.GetAsync("/api/v1/Leads/catalogs"));
        return new Company(companyId, admin, adminId, catalogs);
    }

    private static object NewLead(Company c, Guid? personId = null, string? newPerson = null, object? extra = null) => new
    {
        personId,
        newPerson = newPerson is null ? null : new { personType = 2, firstName = newPerson, lastName = "SRL", commercialName = newPerson, phone = "809-555-0101" },
        topic = "Punto de venta",
        relationshipTypeId = c.Relationship("PROSPECT_CUSTOMER"),
        leadSourceId = c.Source("WEB")
    };

    private static async Task<JsonElement> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/Leads", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await DataAsync(response);
    }

    private static async Task<Guid> NewLeadIdAsync(Company c, HttpClient? client = null, string person = "Ferretería") =>
        (await CreateAsync(client ?? c.Admin, NewLead(c, newPerson: $"{person} {Guid.NewGuid():N}"[..20]))).GetProperty("lead").GetProperty("id").GetGuid();

    private async Task<(HttpClient Client, Guid UserId)> MemberAsync(Company c) => await AddUserAsync(_factory, c.Id, "UsuarioSimple");

    private async Task MembershipAsync(Guid companyId, Guid userId, Guid teamId, string roleCode, bool primary = true)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var role = await db.TeamRoles.IgnoreQueryFilters().SingleAsync(r => r.CompanyId == companyId && r.Code == roleCode && r.GcRecord == 0);
        db.SalesTeamMembers.Add(new SalesTeamMember
        {
            CompanyId = companyId, SalesTeamId = teamId, UserId = userId, TeamRoleId = role.Id, IsPrimary = primary, ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Create_WithExistingOrNewPerson_StartsAtLD000001_InTheInitialStage_WithTheThreeHistories()
    {
        var c = await CompanyAsync();

        var created = await CreateAsync(c.Admin, NewLead(c, newPerson: "Ferretería El Puente"));
        var lead = created.GetProperty("lead");
        lead.GetProperty("code").GetString().Should().Be("LD-000001");
        lead.GetProperty("status").GetInt32().Should().Be(1);
        lead.GetProperty("leadStageId").GetGuid().Should().Be(c.Stage("NEW"));
        var personId = lead.GetProperty("personId").GetGuid();

        var second = await CreateAsync(c.Admin, NewLead(c, personId));
        second.GetProperty("lead").GetProperty("code").GetString().Should().Be("LD-000002");
        second.GetProperty("duplicateOpenLeads").GetArrayLength().Should().Be(1, "default policy Warn");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = lead.GetProperty("id").GetGuid();
        (await db.LeadStageHistory.IgnoreQueryFilters().CountAsync(h => h.LeadId == id)).Should().Be(1);
        (await db.LeadOwnershipHistory.IgnoreQueryFilters().CountAsync(h => h.LeadId == id)).Should().Be(1);
        (await db.LeadStatusHistory.IgnoreQueryFilters().CountAsync(h => h.LeadId == id)).Should().Be(1);
        (await db.Persons.IgnoreQueryFilters().SingleAsync(p => p.Id == personId)).IdentificationNumber.Should().BeNull();
    }

    [Fact]
    public async Task Team_FollowsTheOwnersMemberships()
    {
        var c = await CompanyAsync();
        var a = (await DataAsync(await c.Admin.PostAsJsonAsync("/api/v1/SalesTeams", new { name = "A" }))).GetProperty("id").GetGuid();
        var b = (await DataAsync(await c.Admin.PostAsJsonAsync("/api/v1/SalesTeams", new { name = "B" }))).GetProperty("id").GetGuid();

        (await CreateAsync(c.Admin, NewLead(c, newPerson: "Sin grupo"))).GetProperty("lead").GetProperty("salesTeamId").ValueKind.Should().Be(JsonValueKind.Null);

        await MembershipAsync(c.Id, c.AdminId, a, "MEMBER", primary: false);
        (await CreateAsync(c.Admin, NewLead(c, newPerson: "Un grupo"))).GetProperty("lead").GetProperty("salesTeamId").GetGuid().Should().Be(a);

        await MembershipAsync(c.Id, c.AdminId, b, "MEMBER", primary: true);
        (await CreateAsync(c.Admin, NewLead(c, newPerson: "Principal"))).GetProperty("lead").GetProperty("salesTeamId").GetGuid().Should().Be(b);
        var chosen = new { newPerson = new { personType = 2, firstName = "Elegido", lastName = "SRL", commercialName = "Elegido" }, topic = "T",
            relationshipTypeId = c.Relationship("PROSPECT_CUSTOMER"), leadSourceId = c.Source("WEB"), salesTeamId = a };
        (await CreateAsync(c.Admin, chosen)).GetProperty("lead").GetProperty("salesTeamId").GetGuid().Should().Be(a);
    }

    [Fact]
    public async Task Duplicates_WarnBlockAllow()
    {
        var c = await CompanyAsync();
        var personId = (await CreateAsync(c.Admin, NewLead(c, newPerson: "Duplicada"))).GetProperty("lead").GetProperty("personId").GetGuid();

        (await CreateAsync(c.Admin, NewLead(c, personId))).GetProperty("duplicateOpenLeads").GetArrayLength().Should().Be(1);
        (await DataAsync(await c.Admin.GetAsync($"/api/v1/Leads/duplicates?personId={personId}"))).GetArrayLength().Should().Be(2);

        (await c.Admin.PutAsJsonAsync("/api/v1/LeadCompanySettings", new { duplicateOpenLeadPolicy = 2, requireIdentificationForCustomer = true, codePrefix = "LD-", codeSequenceLength = 6 })).EnsureSuccessStatusCode();
        var blocked = await c.Admin.PostAsJsonAsync("/api/v1/Leads", NewLead(c, personId));
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await blocked.Content.ReadAsStringAsync()).Should().Contain("LEAD_DUPLICATE_OPEN");

        (await c.Admin.PutAsJsonAsync("/api/v1/LeadCompanySettings", new { duplicateOpenLeadPolicy = 3, requireIdentificationForCustomer = true, codePrefix = "LD-", codeSequenceLength = 6 })).EnsureSuccessStatusCode();
        (await CreateAsync(c.Admin, NewLead(c, personId))).GetProperty("duplicateOpenLeads").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Stages_MoveToTheNextOrBack_AndReorderingChangesTheNext()
    {
        var c = await CompanyAsync();
        var id = await NewLeadIdAsync(c);

        (await c.Admin.PutAsJsonAsync($"/api/v1/Leads/{id}/stage", new { leadStageId = c.Stage("CONTACTED") })).EnsureSuccessStatusCode();
        (await c.Admin.PutAsJsonAsync($"/api/v1/Leads/{id}/stage", new { leadStageId = c.Stage("QUALIFIED") })).EnsureSuccessStatusCode();
        (await c.Admin.PutAsJsonAsync($"/api/v1/Leads/{id}/stage", new { leadStageId = c.Stage("NEW") })).EnsureSuccessStatusCode();
        var skip = await c.Admin.PutAsJsonAsync($"/api/v1/Leads/{id}/stage", new { leadStageId = c.Stage("QUALIFIED") });
        skip.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await skip.Content.ReadAsStringAsync()).Should().Contain("LEAD_STAGE_SKIP_NOT_ALLOWED");

        (await c.Admin.PutAsJsonAsync($"/api/v1/LeadStages/{c.Stage("QUALIFIED")}", new { code = "QUALIFIED", name = "Calificado", probability = 40, kind = 2, sortOrder = 1 })).EnsureSuccessStatusCode();
        (await c.Admin.PutAsJsonAsync($"/api/v1/LeadStages/{c.Stage("NEW")}", new { code = "NEW", name = "Nuevo", probability = 10, kind = 1, sortOrder = 0 })).EnsureSuccessStatusCode();
        (await c.Admin.PutAsJsonAsync($"/api/v1/Leads/{id}/stage", new { leadStageId = c.Stage("QUALIFIED") })).StatusCode.Should().Be(HttpStatusCode.OK, "after reordering QUALIFIED follows NEW");
    }

    [Fact]
    public async Task Scope_MemberLeaderSupervisor_AndInactiveVisibleToAll()
    {
        var c = await CompanyAsync();
        var team = (await DataAsync(await c.Admin.PostAsJsonAsync("/api/v1/SalesTeams", new { name = "Capital" }))).GetProperty("id").GetGuid();
        await GrantAsync(_factory, c.Id, "UsuarioSimple", "Leads", delete: false);
        var (member, memberId) = await MemberAsync(c);
        var (leader, leaderId) = await MemberAsync(c);
        var (supervisor, supervisorId) = await MemberAsync(c);
        var (outsider, _) = await MemberAsync(c);
        await MembershipAsync(c.Id, memberId, team, "MEMBER");
        await MembershipAsync(c.Id, leaderId, team, "LEADER");
        await MembershipAsync(c.Id, supervisorId, team, "SUPERVISOR");

        var memberLead = await NewLeadIdAsync(c, member, "Del integrante");
        var adminLead = await NewLeadIdAsync(c, null, "Del admin");

        async Task<List<Guid>> InboxAsync(HttpClient client) =>
            (await DataAsync(await client.GetAsync("/api/v1/Leads?status=0&pageSize=100"))).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

        (await InboxAsync(member)).Should().Equal(memberLead);
        (await InboxAsync(leader)).Should().Equal(memberLead);
        (await InboxAsync(supervisor)).Should().BeEquivalentTo(new[] { memberLead, adminLead });
        (await InboxAsync(outsider)).Should().BeEmpty();
        (await outsider.GetAsync($"/api/v1/Leads/{memberLead}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await member.GetAsync($"/api/v1/Leads/{adminLead}/timeline")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await member.PostAsJsonAsync($"/api/v1/Leads/{memberLead}/inactivate", new { leadReasonId = c.Reason("HIGH_PRICE") })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "not an inactivation reason");
        (await member.PostAsJsonAsync($"/api/v1/Leads/{memberLead}/inactivate", new { leadReasonId = c.Reason("NO_ANSWER") })).EnsureSuccessStatusCode();
        (await InboxAsync(outsider)).Should().Equal(memberLead);
        (await outsider.GetAsync($"/api/v1/Leads/{memberLead}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var board = await DataAsync(await supervisor.GetAsync("/api/v1/Leads/board"));
        board.EnumerateArray().Sum(col => col.GetProperty("count").GetInt32()).Should().Be(1, "only the open admin lead");
    }

    [Fact]
    public async Task Reactivate_OtherUserBecomesOwnerWithTheirTeam_AndReassignNeedsCanReassign()
    {
        var c = await CompanyAsync();
        var a = (await DataAsync(await c.Admin.PostAsJsonAsync("/api/v1/SalesTeams", new { name = "A" }))).GetProperty("id").GetGuid();
        var b = (await DataAsync(await c.Admin.PostAsJsonAsync("/api/v1/SalesTeams", new { name = "B" }))).GetProperty("id").GetGuid();
        await GrantAsync(_factory, c.Id, "UsuarioSimple", "Leads", delete: false);
        var (owner, ownerId) = await MemberAsync(c);
        var (other, otherId) = await MemberAsync(c);
        var (leader, leaderId) = await MemberAsync(c);
        await MembershipAsync(c.Id, ownerId, a, "MEMBER");
        await MembershipAsync(c.Id, otherId, b, "MEMBER");
        await MembershipAsync(c.Id, leaderId, a, "LEADER");

        var id = await NewLeadIdAsync(c, owner);
        var forbidden = await owner.PostAsJsonAsync($"/api/v1/Leads/{id}/reassign", new { newOwnerUserId = otherId });
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await forbidden.Content.ReadAsStringAsync()).Should().Contain("LEAD_ACTION_FORBIDDEN");
        var reassigned = await DataAsync(await leader.PostAsJsonAsync($"/api/v1/Leads/{id}/reassign", new { newOwnerUserId = leaderId, notes = "cubro" }));
        reassigned.GetProperty("ownerUserId").GetGuid().Should().Be(leaderId);
        reassigned.GetProperty("salesTeamId").GetGuid().Should().Be(a, "reassigning keeps the team");

        (await leader.PostAsJsonAsync($"/api/v1/Leads/{id}/inactivate", new { leadReasonId = c.Reason("NO_ANSWER") })).EnsureSuccessStatusCode();
        var reactivated = await DataAsync(await other.PostAsync($"/api/v1/Leads/{id}/reactivate", null));
        reactivated.GetProperty("ownerUserId").GetGuid().Should().Be(otherId);
        reactivated.GetProperty("salesTeamId").GetGuid().Should().Be(b);

        await using var scope = _factory.Services.CreateAsyncScope();
        var history = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().LeadOwnershipHistory.IgnoreQueryFilters()
            .Where(h => h.LeadId == id).OrderBy(h => h.At).ToListAsync();
        history.Select(h => h.Cause).Should().Equal(LeadOwnershipCause.Created, LeadOwnershipCause.Reassigned, LeadOwnershipCause.Reactivated);
        history.Last().FromUserId.Should().Be(leaderId);
        history.Last().FromTeamId.Should().Be(a);
    }

    [Fact]
    public async Task Products_InUsd_StoreRateAndBaseValue_AndLockTheCurrency()
    {
        var c = await CompanyAsync();
        var usdId = await CurrencyIdAsync("USD");
        (await c.Admin.PostAsJsonAsync("/api/v1/CompanyCurrencies", new { currencyId = usdId })).EnsureSuccessStatusCode();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        (await c.Admin.PostAsJsonAsync("/api/v1/ExchangeRates", new { currencyId = usdId, rateDate = today.AddDays(-1).ToString("yyyy-MM-dd"), rate = 59.3612m })).EnsureSuccessStatusCode();
        var product = await DataAsync(await c.Admin.PostAsJsonAsync("/api/v1/Products", new { code = "CRM-USR", name = "Licencia", kind = 2, referencePrice = 1450m }));

        var lead = (await CreateAsync(c.Admin, new
        {
            newPerson = new { personType = 2, firstName = "Óptica", lastName = "Visión", commercialName = "Óptica Visión" },
            topic = "Licencias", relationshipTypeId = c.Relationship("PROSPECT_CUSTOMER"), leadSourceId = c.Source("REFERRAL"), currencyId = usdId
        })).GetProperty("lead");
        var id = lead.GetProperty("id").GetGuid();

        var line = await DataAsync(await c.Admin.PostAsJsonAsync($"/api/v1/Leads/{id}/products", new { productId = product.GetProperty("id").GetGuid(), quantity = 10 }));
        line.GetProperty("unitPrice").GetDecimal().Should().Be(Math.Round(1450m / 59.3612m, 2, MidpointRounding.AwayFromZero));
        var rejected = await DataAsync(await c.Admin.PostAsJsonAsync($"/api/v1/Leads/{id}/products", new { productId = product.GetProperty("id").GetGuid(), quantity = 1, unitPrice = 500, status = 3 }));

        var detail = await DataAsync(await c.Admin.GetAsync($"/api/v1/Leads/{id}"));
        var value = line.GetProperty("lineTotal").GetDecimal();
        detail.GetProperty("estimatedValue").GetDecimal().Should().Be(value, "the rejected line does not add");
        detail.GetProperty("exchangeRate").GetDecimal().Should().Be(59.3612m);
        detail.GetProperty("exchangeRateDate").GetString().Should().Be(today.AddDays(-1).ToString("yyyy-MM-dd"));
        detail.GetProperty("estimatedValueBase").GetDecimal().Should().Be(Math.Round(value * 59.3612m, 2, MidpointRounding.AwayFromZero));
        detail.GetProperty("currencyCode").GetString().Should().Be("USD");
        detail.GetProperty("baseCurrencyCode").GetString().Should().Be("DOP");
        detail.GetProperty("products").GetArrayLength().Should().Be(2);
        rejected.GetProperty("status").GetInt32().Should().Be(3);

        var locked = await c.Admin.PutAsJsonAsync($"/api/v1/Leads/{id}", new { topic = "Licencias", relationshipTypeId = c.Relationship("PROSPECT_CUSTOMER"), leadSourceId = c.Source("REFERRAL"), currencyId = await CurrencyIdAsync("DOP") });
        locked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await locked.Content.ReadAsStringAsync()).Should().Contain("LEAD_CURRENCY_LOCKED");

        var baseChange = await c.Admin.PutAsJsonAsync(
            $"/api/v1/CompanyCurrencies/{(await DataAsync(await c.Admin.GetAsync("/api/v1/CompanyCurrencies"))).EnumerateArray().Single(x => x.GetProperty("code").GetString() == "USD").GetProperty("id").GetGuid()}",
            new { isBase = true, isActive = true });
        baseChange.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await baseChange.Content.ReadAsStringAsync()).Should().Contain("BASE_CURRENCY_IN_USE").And.Contain("Leads");
    }

    [Fact]
    public async Task Interactions_Comments_AndTimeline()
    {
        var c = await CompanyAsync();
        var id = await NewLeadIdAsync(c);

        var noDuration = await c.Admin.PostAsJsonAsync($"/api/v1/Leads/{id}/interactions", new { contactTypeCompanyId = c.ContactType("CALL"), summary = "Llamada" });
        noDuration.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noDuration.Content.ReadAsStringAsync()).Should().Contain("LEAD_INTERACTION_DURATION_REQUIRED");

        var at = DateTime.UtcNow.AddMinutes(-30);
        var interaction = await DataAsync(await c.Admin.PostAsJsonAsync($"/api/v1/Leads/{id}/interactions",
            new { contactTypeCompanyId = c.ContactType("CALL"), durationMinutes = 10, summary = "Llamada", occurredAt = at, outcome = 1 }));
        (await c.Admin.PostAsJsonAsync($"/api/v1/Leads/{id}/comments", new { body = "Interesado", leadInteractionId = interaction.GetProperty("id").GetGuid() })).EnsureSuccessStatusCode();
        (await c.Admin.PutAsJsonAsync($"/api/v1/Leads/{id}/stage", new { leadStageId = c.Stage("CONTACTED") })).EnsureSuccessStatusCode();

        var detail = await DataAsync(await c.Admin.GetAsync($"/api/v1/Leads/{id}"));
        detail.GetProperty("lastInteractionAt").GetDateTime().Should().BeCloseTo(at, TimeSpan.FromSeconds(1));
        detail.GetProperty("lastContactTypeName").GetString().Should().Be("Llamada");
        detail.GetProperty("interactionsCount").GetInt32().Should().Be(1);
        detail.GetProperty("personPhone").GetString().Should().Be("809-555-0101");

        var timeline = (await DataAsync(await c.Admin.GetAsync($"/api/v1/Leads/{id}/timeline"))).EnumerateArray().ToList();
        timeline.Select(t => t.GetProperty("kind").GetString()).Should().Contain(new[] { "Interaction", "Comment", "Stage", "Ownership", "Status" });
        timeline.Select(t => t.GetProperty("at").GetDateTime()).Should().BeInDescendingOrder();
        (await DataAsync(await c.Admin.GetAsync($"/api/v1/Leads/{id}/interactions"))).EnumerateArray().Single().GetProperty("contactTypeCode").GetString().Should().Be("CALL");

        (await c.Admin.PostAsJsonAsync($"/api/v1/Leads/{id}/inactivate", new { leadReasonId = c.Reason("POSTPONED") })).EnsureSuccessStatusCode();
        (await c.Admin.PostAsJsonAsync($"/api/v1/Leads/{id}/interactions", new { contactTypeCompanyId = c.ContactType("EMAIL"), summary = "x" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task InUse_CatalogsAndPersons_Answer409()
    {
        var c = await CompanyAsync();
        var product = (await DataAsync(await c.Admin.PostAsJsonAsync("/api/v1/Products", new { code = "SOP-12", name = "Soporte", kind = 2 }))).GetProperty("id").GetGuid();
        var lead = (await CreateAsync(c.Admin, new
        {
            newPerson = new { personType = 2, firstName = "Con", lastName = "Uso", commercialName = "Con uso" },
            topic = "Soporte", relationshipTypeId = c.Relationship("PROSPECT_CUSTOMER"), leadSourceId = c.Source("EVENT"),
            productLines = new[] { new { productId = product, quantity = 1 } },
            firstInteraction = new { contactTypeCompanyId = c.ContactType("EMAIL"), summary = "Enviado" }
        })).GetProperty("lead");

        async Task Expect409(HttpResponseMessage response, string detail)
        {
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
            body.Should().Contain(detail);
        }

        await Expect409(await c.Admin.DeleteAsync($"/api/v1/Products/{product}"), "Active LeadProducts: 1");
        await Expect409(await c.Admin.DeleteAsync($"/api/v1/LeadSources/{c.Source("EVENT")}"), "Active Leads: 1");
        await Expect409(await c.Admin.DeleteAsync($"/api/v1/RelationshipTypes/{c.Relationship("PROSPECT_CUSTOMER")}"), "RELATIONSHIP_TYPE_IN_USE");
        await Expect409(await c.Admin.DeleteAsync($"/api/v1/Persons/{lead.GetProperty("personId").GetGuid()}"), "Active Leads: 1");
        var master = (await DataAsync(await c.Admin.GetAsync("/api/v1/ContactTypes"))).EnumerateArray().Single(t => t.GetProperty("code").GetString() == "EMAIL").GetProperty("id").GetGuid();
        await Expect409(await c.Admin.DeleteAsync($"/api/v1/ContactTypes/{master}"), "Active LeadInteractions");
        (await c.Admin.DeleteAsync($"/api/v1/LeadSources/{c.Source("COLD")}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Permissions_AndProductsLookup()
    {
        using var agent = await JOIN.IntegrationTests.Security.ModuleTestHelpers.LoginAsync(_factory, "agent@join.com");
        (await agent.GetAsync("/api/v1/Leads")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var simple = await JOIN.IntegrationTests.Security.ModuleTestHelpers.LoginAsync(_factory, "simpleuser@join.com");
        (await simple.GetAsync("/api/v1/Leads")).StatusCode.Should().Be(HttpStatusCode.OK);
        var lookup = await DataAsync(await simple.GetAsync("/api/v1/Leads/products-lookup?search=crm"));
        lookup.EnumerateArray().Select(p => p.GetProperty("code").GetString()).Should().Contain("CRM-USR");
        (await simple.GetAsync("/api/v1/SalesTeams/me")).StatusCode.Should().Be(HttpStatusCode.OK, "SPEC 60 endpoint under the Leads permission");

        var inbox = await DataAsync(await simple.GetAsync("/api/v1/Leads?status=0"));
        inbox.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("code").GetString()).Should().Contain("LD-000002", "seeded lead of simpleuser");
    }

    [Fact]
    public async Task NewSql_ShouldPlanAgainstTheMigratedSchema()
    {
        var c = await CompanyAsync();
        await NewLeadIdAsync(c);
        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        string[] queries =
        [
            """
            SELECT l.Id, s.Name, lastct.Name FROM customer.leads l
            INNER JOIN admin.persons p ON p.Id = l.PersonId
            INNER JOIN customer.leadstages s ON s.Id = l.LeadStageId
            LEFT JOIN LATERAL (SELECT ctc.Name FROM customer.leadinteractions i INNER JOIN customer.contacttypecompanies ctc ON ctc.Id = i.ContactTypeCompanyId
                               WHERE i.LeadId = l.Id AND i.GcRecord = 0 ORDER BY i.OccurredAt DESC LIMIT 1) lastct ON TRUE
            WHERE l.CompanyId = @company AND l.GcRecord = 0 AND l.Status = 1
              AND (l.Status = 2 OR l.OwnerUserId = @user OR l.SalesTeamId = ANY(@teams))
            ORDER BY l.LastInteractionAt DESC NULLS LAST, l.Created DESC LIMIT 20
            """,
            """
            SELECT s.Id, COUNT(l.Id), COALESCE(SUM(l.EstimatedValueBase), 0) FROM customer.leadstages s
            LEFT JOIN customer.leads l ON l.LeadStageId = s.Id AND l.CompanyId = s.CompanyId AND l.GcRecord = 0 AND l.Status = 1
            WHERE s.CompanyId = @company AND s.GcRecord = 0 AND s.IsActive = TRUE AND s.Kind IN (1, 2) GROUP BY s.Id
            """,
            """
            SELECT x.* FROM (
                SELECT i.Id, i.OccurredAt AS At FROM customer.leadinteractions i WHERE i.LeadId = @lead AND i.GcRecord = 0
                UNION ALL SELECT c.Id, c.Created FROM customer.leadcomments c WHERE c.LeadId = @lead AND c.GcRecord = 0
                UNION ALL SELECT h.Id, h.ChangedAt FROM customer.leadstagehistory h WHERE h.LeadId = @lead AND h.GcRecord = 0
                UNION ALL SELECT h.Id, h.At FROM customer.leadownershiphistory h WHERE h.LeadId = @lead AND h.GcRecord = 0
                UNION ALL SELECT h.Id, h.At FROM customer.leadstatushistory h WHERE h.LeadId = @lead AND h.GcRecord = 0
            ) x ORDER BY x.At DESC LIMIT 200
            """
        ];

        foreach (var sql in queries)
        {
            await using var cmd = new NpgsqlCommand($"EXPLAIN (FORMAT JSON) {sql}", conn);
            cmd.Parameters.AddWithValue("company", c.Id);
            cmd.Parameters.AddWithValue("user", c.AdminId);
            cmd.Parameters.AddWithValue("teams", new[] { Guid.NewGuid() });
            cmd.Parameters.AddWithValue("lead", Guid.NewGuid());
            ((string?)await cmd.ExecuteScalarAsync()).Should().Contain("Plan");
        }
    }

    private async Task<Guid> CurrencyIdAsync(string code)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Currencies.IgnoreQueryFilters().Where(x => x.Code == code && x.GcRecord == 0).Select(x => x.Id).SingleAsync();
    }
}
