// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JOIN.Domain.Admin;
using JOIN.Domain.Customers;
using JOIN.Domain.Customers.Leads;
using JOIN.Domain.Enums;
using JOIN.Persistence;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static JOIN.IntegrationTests.Customers.CommercialTestHelpers;

namespace JOIN.IntegrationTests.Customers;

/// <summary>
/// SPEC 63 — win, lose, reopen, customer with origin Lead, post-sale customer stages and the LEAD/PROSPECT migration,
/// against PostgreSQL 17 (criteria 1–10).
/// </summary>
public sealed class LeadWinLossPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public LeadWinLossPostgreSqlTests(CustomWebApplicationFactory factory) => _factory = factory;

    private sealed record Setup(Guid CompanyId, HttpClient Client, Guid AdminId, JsonElement Catalogs)
    {
        public Guid Id(string list, string code) =>
            Catalogs.GetProperty(list).EnumerateArray().Single(i => i.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();
    }

    private async Task<Setup> SetupAsync()
    {
        var (companyId, client, adminId) = await CreateCompanyWithUserAsync(_factory);
        return new Setup(companyId, client, adminId, await DataAsync(await client.GetAsync("/api/v1/Leads/catalogs")));
    }

    private static async Task<JsonElement> LeadAsync(Setup s, string relationship = "PROSPECT_CUSTOMER", Guid? personId = null, object? currencyId = null, string? name = null)
    {
        var response = await s.Client.PostAsJsonAsync("/api/v1/Leads", new
        {
            personId,
            newPerson = personId is null ? new { personType = 2, firstName = name ?? "Cliente", lastName = "SRL", commercialName = $"{name ?? "Cliente"} {Guid.NewGuid():N}"[..24] } : null,
            topic = "Licencias", relationshipTypeId = s.Id("relationshipTypes", relationship), leadSourceId = s.Id("sources", "WEB"), currencyId, estimatedValue = 1000
        });
        return (await DataAsync(response)).GetProperty("lead");
    }

    private async Task<Guid> IdTypeAsync(string name)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().IdentificationTypes.IgnoreQueryFilters().Where(t => t.Name == name).Select(t => t.Id).FirstAsync();
    }

    [Fact]
    public async Task Win_FromContacted_CreatesTheCustomer_ThenConfirms_AndCustomersShowTheOrigin()
    {
        var s = await SetupAsync();
        await s.Client.PutAsJsonAsync("/api/v1/LeadCompanySettings", new { duplicateOpenLeadPolicy = 3, requireIdentificationForCustomer = false, codePrefix = "LD-", codeSequenceLength = 6 });
        var lead = await LeadAsync(s);
        var id = lead.GetProperty("id").GetGuid();
        (await s.Client.PutAsJsonAsync($"/api/v1/Leads/{id}/stage", new { leadStageId = s.Id("stages", "CONTACTED") })).EnsureSuccessStatusCode();

        var won = await DataAsync(await s.Client.PostAsJsonAsync($"/api/v1/Leads/{id}/win", new { leadReasonId = s.Id("reasons", "BEST_PRICE") }));

        won.GetProperty("lead").GetProperty("leadStageId").GetGuid().Should().Be(s.Id("stages", "WON"));
        won.GetProperty("customerAction").GetString().Should().Be("Created");
        var customerId = won.GetProperty("customerId").GetGuid();
        var history = (await DataAsync(await s.Client.GetAsync($"/api/v1/Customers/{customerId}/status-history"))).GetProperty("items").EnumerateArray().ToList();
        history.Should().ContainSingle(h => h.GetProperty("source").GetInt32() == 5 && h.GetProperty("sourceReferenceCode").GetString() == lead.GetProperty("code").GetString());

        var second = await LeadAsync(s, personId: lead.GetProperty("personId").GetGuid());
        var again = await DataAsync(await s.Client.PostAsJsonAsync($"/api/v1/Leads/{second.GetProperty("id").GetGuid()}/win", new { leadReasonId = s.Id("reasons", "FEATURES") }));
        again.GetProperty("customerAction").GetString().Should().Be("Confirmed");
        again.GetProperty("customerId").GetGuid().Should().Be(customerId);

        var customers = (await DataAsync(await s.Client.GetAsync("/api/v1/Customers?pageSize=50"))).GetProperty("items").EnumerateArray().ToList();
        customers.Single(c => c.GetProperty("id").GetGuid() == customerId).GetProperty("originLeadCode").GetString().Should().Be(lead.GetProperty("code").GetString());
        (await DataAsync(await s.Client.GetAsync($"/api/v1/Customers/{customerId}"))).GetProperty("originLeadId").GetGuid().Should().Be(id);
    }

    [Fact]
    public async Task Win_WithASupplier_DoesNotCreateACustomer()
    {
        var s = await SetupAsync();
        var lead = await LeadAsync(s, "SUPPLIER");

        var won = await DataAsync(await s.Client.PostAsJsonAsync($"/api/v1/Leads/{lead.GetProperty("id").GetGuid()}/win", new { leadReasonId = s.Id("reasons", "BEST_PRICE") }));

        won.GetProperty("customerId").ValueKind.Should().Be(JsonValueKind.Null);
        won.GetProperty("lead").GetProperty("status").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Win_WithoutIdentification_Is400UntilItArrives()
    {
        var s = await SetupAsync();
        var lead = await LeadAsync(s);
        var id = lead.GetProperty("id").GetGuid();

        var blocked = await s.Client.PostAsJsonAsync($"/api/v1/Leads/{id}/win", new { leadReasonId = s.Id("reasons", "BEST_PRICE") });
        blocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await blocked.Content.ReadAsStringAsync()).Should().Contain("PERSON_IDENTIFICATION_REQUIRED");

        var rnc = await IdTypeAsync("RNC");
        var won = await s.Client.PostAsJsonAsync($"/api/v1/Leads/{id}/win", new { leadReasonId = s.Id("reasons", "BEST_PRICE"), identificationTypeId = rnc, identificationNumber = $"1-{Random.Shared.Next(10, 99)}-{Random.Shared.Next(10000, 99999)}-1" });
        won.StatusCode.Should().Be(HttpStatusCode.OK, await won.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Win_AcceptedLinesAndTheClosingRateInUsd()
    {
        var s = await SetupAsync();
        await s.Client.PutAsJsonAsync("/api/v1/LeadCompanySettings", new { duplicateOpenLeadPolicy = 1, requireIdentificationForCustomer = false, codePrefix = "LD-", codeSequenceLength = 6 });
        Guid usd;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            usd = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Currencies.IgnoreQueryFilters().Where(c => c.Code == "USD").Select(c => c.Id).SingleAsync();
        }

        (await s.Client.PostAsJsonAsync("/api/v1/CompanyCurrencies", new { currencyId = usd })).EnsureSuccessStatusCode();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        (await s.Client.PostAsJsonAsync("/api/v1/ExchangeRates", new { currencyId = usd, rateDate = today.AddDays(-3).ToString("yyyy-MM-dd"), rate = 59m })).EnsureSuccessStatusCode();
        var product = (await DataAsync(await s.Client.PostAsJsonAsync("/api/v1/Products", new { code = "CRM", name = "CRM", kind = 2, referencePrice = 5900m }))).GetProperty("id").GetGuid();
        var lead = await LeadAsync(s, currencyId: usd);
        var id = lead.GetProperty("id").GetGuid();
        var a = (await DataAsync(await s.Client.PostAsJsonAsync($"/api/v1/Leads/{id}/products", new { productId = product, quantity = 2 }))).GetProperty("id").GetGuid();
        var b = (await DataAsync(await s.Client.PostAsJsonAsync($"/api/v1/Leads/{id}/products", new { productId = product, quantity = 5 }))).GetProperty("id").GetGuid();
        (await s.Client.PostAsJsonAsync("/api/v1/ExchangeRates", new { currencyId = usd, rateDate = today.ToString("yyyy-MM-dd"), rate = 60m })).EnsureSuccessStatusCode();

        var won = (await DataAsync(await s.Client.PostAsJsonAsync($"/api/v1/Leads/{id}/win", new { leadReasonId = s.Id("reasons", "BEST_PRICE"), acceptedLeadProductIds = new[] { a } }))).GetProperty("lead");

        won.GetProperty("wonValue").GetDecimal().Should().Be(200m, "2 × 100 USD");
        won.GetProperty("wonExchangeRate").GetDecimal().Should().Be(60m);
        won.GetProperty("wonValueBase").GetDecimal().Should().Be(12000m);
        won.GetProperty("estimatedValueBase").GetDecimal().Should().Be(41300m, "the funnel keeps the rate of the day the lines were entered");
        var products = (await DataAsync(await s.Client.GetAsync($"/api/v1/Leads/{id}"))).GetProperty("products").EnumerateArray().ToList();
        products.Single(p => p.GetProperty("id").GetGuid() == a).GetProperty("status").GetInt32().Should().Be(2);
        products.Single(p => p.GetProperty("id").GetGuid() == b).GetProperty("status").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Reactivated_ThenWon_CountsForTheWinnerAndTheirTeam()
    {
        var s = await SetupAsync();
        await s.Client.PutAsJsonAsync("/api/v1/LeadCompanySettings", new { duplicateOpenLeadPolicy = 1, requireIdentificationForCustomer = false, codePrefix = "LD-", codeSequenceLength = 6 });
        var team = (await DataAsync(await s.Client.PostAsJsonAsync("/api/v1/SalesTeams", new { name = "Norte" }))).GetProperty("id").GetGuid();
        await GrantAsync(_factory, s.CompanyId, "UsuarioSimple", "Leads");
        var (other, otherId) = await AddUserAsync(_factory, s.CompanyId, "UsuarioSimple");
        var roles = (await DataAsync(await s.Client.GetAsync("/api/v1/TeamRoles"))).EnumerateArray().ToDictionary(r => r.GetProperty("code").GetString()!, r => r.GetProperty("id").GetGuid());
        (await s.Client.PostAsJsonAsync($"/api/v1/SalesTeams/{team}/members", new { userId = otherId, teamRoleId = roles["MEMBER"] })).EnsureSuccessStatusCode();
        var lead = await LeadAsync(s);
        var id = lead.GetProperty("id").GetGuid();
        (await s.Client.PostAsJsonAsync($"/api/v1/Leads/{id}/inactivate", new { leadReasonId = s.Id("reasons", "POSTPONED") })).EnsureSuccessStatusCode();
        (await other.PostAsync($"/api/v1/Leads/{id}/reactivate", null)).EnsureSuccessStatusCode();

        var won = await DataAsync(await other.PostAsJsonAsync($"/api/v1/Leads/{id}/win", new { leadReasonId = s.Id("reasons", "BEST_PRICE") }));

        won.GetProperty("lead").GetProperty("salesTeamId").GetGuid().Should().Be(team);
        await using var scope = _factory.Services.CreateAsyncScope();
        var customer = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == won.GetProperty("customerId").GetGuid());
        customer.UserId.Should().Be(otherId);
    }

    [Fact]
    public async Task Lose_NeedsALossReason_ReopenGoesBack_AndAWonLeadIsNotReopened()
    {
        var s = await SetupAsync();
        var id = (await LeadAsync(s, "SUPPLIER")).GetProperty("id").GetGuid();
        (await s.Client.PutAsJsonAsync($"/api/v1/Leads/{id}/stage", new { leadStageId = s.Id("stages", "CONTACTED") })).EnsureSuccessStatusCode();

        (await s.Client.PostAsJsonAsync($"/api/v1/Leads/{id}/lose", new { leadReasonId = s.Id("reasons", "BEST_PRICE") })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync($"/api/v1/Leads/{id}/lose", new { leadReasonId = s.Id("reasons", "COMPETITOR"), competitorName = "Otro CRM" })).EnsureSuccessStatusCode();
        var reopened = await DataAsync(await s.Client.PostAsync($"/api/v1/Leads/{id}/reopen", null));
        reopened.GetProperty("leadStageId").GetGuid().Should().Be(s.Id("stages", "CONTACTED"));
        reopened.GetProperty("status").GetInt32().Should().Be(1);

        (await s.Client.PostAsJsonAsync($"/api/v1/Leads/{id}/win", new { leadReasonId = s.Id("reasons", "BEST_PRICE") })).EnsureSuccessStatusCode();
        var notLost = await s.Client.PostAsync($"/api/v1/Leads/{id}/reopen", null);
        notLost.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await notLost.Content.ReadAsStringAsync()).Should().Contain("LEAD_NOT_LOST");
    }

    [Fact]
    public async Task Migration_TurnsAProspectCustomerIntoAnOpenLead_AndWinningReactivatesTheSameCustomer()
    {
        var s = await SetupAsync();
        await s.Client.PutAsJsonAsync("/api/v1/LeadCompanySettings", new { duplicateOpenLeadPolicy = 1, requireIdentificationForCustomer = false, codePrefix = "LD-", codeSequenceLength = 6 });
        Guid customerId;
        string customerCode;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var prospect = CustomerStage.Create(s.CompanyId, CustomerStage.LegacyCodes.Prospect, "En proceso", null, 9, isDefault: false);
            var person = new Person { CompanyId = s.CompanyId, PersonType = PersonType.Legal, FirstName = "Migrada", LastName = "SRL", CommercialName = "Migrada SRL" };
            db.CustomerStages.Add(prospect);
            db.Persons.Add(person);
            var customer = Customer.Create(s.CompanyId, person.Id, s.AdminId, $"C9{Random.Shared.Next(10000000, 99999999)}", prospect.Id);
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            (customerId, customerCode) = (customer.Id, customer.CustomerCode);

            var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
            await seeder.MigrateFunnelCustomersToLeadsAsync();
            await seeder.MigrateFunnelCustomersToLeadsAsync();
        }

        await using (var check = _factory.Services.CreateAsyncScope())
        {
            var db = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var customer = await db.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == customerId);
            customer.IsActive.Should().BeFalse();
            var defaultStage = await db.CustomerStages.IgnoreQueryFilters().SingleAsync(st => st.CompanyId == s.CompanyId && st.IsDefault && st.GcRecord == 0);
            customer.CustomerStageId.Should().Be(defaultStage.Id);
            var leads = await db.Leads.IgnoreQueryFilters().Where(l => l.CustomerId == customerId).ToListAsync();
            leads.Should().ContainSingle("running the migration twice creates nothing new");
            leads[0].Status.Should().Be(LeadStatus.Open);
            leads[0].LeadStageId.Should().Be(s.Id("stages", "QUALIFIED"));
            (await db.CustomerStatusHistory.IgnoreQueryFilters().Where(h => h.CustomerId == customerId).ToListAsync())
                .Should().ContainSingle(h => h.Action == CustomerStatusAction.Deactivated && h.Source == CustomerStatusSource.Migration && h.SourceReferenceId == leads[0].Id);
            (await db.CustomerStages.IgnoreQueryFilters().AnyAsync(st => st.CompanyId == s.CompanyId && st.GcRecord == 0 && st.Code == CustomerStage.LegacyCodes.Prospect)).Should().BeFalse();

            var won = await DataAsync(await s.Client.PostAsJsonAsync($"/api/v1/Leads/{leads[0].Id}/win", new { leadReasonId = s.Id("reasons", "BEST_PRICE") }));
            won.GetProperty("customerAction").GetString().Should().Be("Activated");
            won.GetProperty("customerId").GetGuid().Should().Be(customerId);
            won.GetProperty("customerCode").GetString().Should().Be(customerCode);
        }
    }

    [Fact]
    public async Task Stages_NoCompanyKeepsLeadOrProspect_AndNewCompaniesArePostSale()
    {
        var s = await SetupAsync();
        var codes = (await DataAsync(await s.Client.GetAsync("/api/v1/CustomerStages"))).EnumerateArray().Select(st => st.GetProperty("code").GetString()).ToList();
        codes.Should().Equal("CUSTOMER", "RECURRING", "FORMER");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.CustomerStages.IgnoreQueryFilters().AnyAsync(st => st.GcRecord == 0 && (st.Code == "LEAD" || st.Code == "PROSPECT"))).Should().BeFalse();
    }
}
