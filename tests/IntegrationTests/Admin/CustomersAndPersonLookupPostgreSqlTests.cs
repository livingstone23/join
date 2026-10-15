// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.UseCases.Admin.Persons.Queries;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Application.UseCases.Security.Auth.Register;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Customers;
using JOIN.Domain.Enums;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace JOIN.IntegrationTests.Admin;

/// <summary>
/// SPEC 54 (person lookup and seed) and SPEC 55 (one customer per person, idempotent activation, history, stages)
/// against the migrated and seeded PostgreSQL 17 database.
/// </summary>
public sealed class CustomersAndPersonLookupPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "Integration!Pass123";
    private const string Creator = "spec55-tests";

    private readonly CustomWebApplicationFactory _factory;

    public CustomersAndPersonLookupPostgreSqlTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ---------------------------------------------------------------- SPEC 54 — seed

    [Fact]
    public async Task Seed_ShouldCreateTheLookupPersonsAndThirtyCustomersWithTheirHistory()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var joinId = await JoinCompanyIdAsync(db);
        var registeringUser = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == "livingstone23@gmail.com");

        var idTypes = await db.IdentificationTypes.IgnoreQueryFilters().Select(t => t.Name).ToListAsync();
        idTypes.Should().Contain(["Cedula", "Pasaporte", "RNC"]);

        var customers = await db.Customers.IgnoreQueryFilters().Where(c => c.CompanyId == joinId).ToListAsync();
        customers.Should().HaveCount(30);
        customers.Count(c => !c.IsActive).Should().Be(2);
        customers.Should().OnlyContain(c => c.UserId == registeringUser.Id && c.GcRecord == 0);
        customers.Select(c => c.CustomerCode).Should().OnlyHaveUniqueItems().And.Contain("C000000001");

        var customerIds = customers.Select(c => c.Id).ToList();
        var history = await db.CustomerStatusHistory.IgnoreQueryFilters().Where(h => customerIds.Contains(h.CustomerId)).ToListAsync();
        history.Should().HaveCount(32);
        history.Count(h => h.Action == CustomerStatusAction.Created).Should().Be(30);
        history.Count(h => h.Action == CustomerStatusAction.Deactivated).Should().Be(2);
        history.Should().OnlyContain(h => h.Source == CustomerStatusSource.Seed);

        var seededCompanies = await db.Companies.IgnoreQueryFilters()
            .Where(c => c.TaxId == "JOIN-001" || c.TaxId == "PRIV-001")
            .Select(c => c.Id)
            .ToListAsync();
        var stagesByCompany = await db.CustomerStages.IgnoreQueryFilters()
            .Where(s => s.GcRecord == 0 && seededCompanies.Contains(s.CompanyId))
            .GroupBy(s => s.CompanyId)
            .Select(g => new { Count = g.Count(), Defaults = g.Count(s => s.IsDefault) })
            .ToListAsync();
        stagesByCompany.Should().HaveCount(2).And.OnlyContain(s => s.Count == 4 && s.Defaults == 1);
    }

    [Fact]
    public async Task Seed_RunTwice_ShouldNotDuplicateAnything()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var joinId = await JoinCompanyIdAsync(db);
        var before = (
            Persons: await db.Persons.IgnoreQueryFilters().CountAsync(p => p.CompanyId == joinId),
            Customers: await db.Customers.IgnoreQueryFilters().CountAsync(),
            History: await db.CustomerStatusHistory.IgnoreQueryFilters().CountAsync(),
            Types: await db.IdentificationTypes.IgnoreQueryFilters().CountAsync(),
            Stages: await db.CustomerStages.IgnoreQueryFilters().CountAsync());

        await scope.ServiceProvider.GetRequiredService<JOIN.Persistence.DatabaseSeeder>().SeedAsync();

        var after = (
            Persons: await db.Persons.IgnoreQueryFilters().CountAsync(p => p.CompanyId == joinId),
            Customers: await db.Customers.IgnoreQueryFilters().CountAsync(),
            History: await db.CustomerStatusHistory.IgnoreQueryFilters().CountAsync(),
            Types: await db.IdentificationTypes.IgnoreQueryFilters().CountAsync(),
            Stages: await db.CustomerStages.IgnoreQueryFilters().CountAsync());
        after.Should().Be(before);
    }

    // ---------------------------------------------------------------- SPEC 54 — lookup

    [Fact]
    public async Task Lookup_ByLastName_ShouldListEachPersonOnceWithCustomersFirst()
    {
        using var client = await CreateSuperAdminClientAsync(await JoinCompanyIdAsync());

        var rows = await LookupAsync(client, "cano");

        rows.Select(r => r.GetProperty("personId").GetGuid()).Should().OnlyHaveUniqueItems();
        var names = rows.Select(r => r.GetProperty("fullName").GetString()).ToList();
        names.Should().Contain(["Livingstone Cano", "Gabriela Cano Ferreira", "Mateo Cano Ruiz", "Elena Rodríguez Cano", "Software Cano Tech S.R.L."]);

        var elena = rows.Single(r => r.GetProperty("fullName").GetString() == "Elena Rodríguez Cano");
        elena.GetProperty("isCustomer").GetBoolean().Should().BeTrue();
        elena.GetProperty("isCustomerActive").GetBoolean().Should().BeFalse("an inactive customer is still a customer");
        var mateo = rows.Single(r => r.GetProperty("fullName").GetString() == "Mateo Cano Ruiz");
        mateo.GetProperty("isCustomer").GetBoolean().Should().BeFalse();
        mateo.GetProperty("customerId").ValueKind.Should().Be(JsonValueKind.Null);
        mateo.GetProperty("customerStageCode").ValueKind.Should().Be(JsonValueKind.Null);
        var livingstone = rows.Single(r => r.GetProperty("fullName").GetString() == "Livingstone Cano");
        livingstone.GetProperty("customerStageCode").GetString().Should().Be(CustomerStage.Codes.Customer);
        livingstone.GetProperty("identificationTypeName").GetString().Should().Be("Pasaporte");

        // Active customers, then inactive customers, then plain persons.
        var groups = rows.Select(r => !r.GetProperty("isCustomer").GetBoolean() ? 2 : r.GetProperty("isCustomerActive").GetBoolean() ? 0 : 1).ToList();
        groups.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Lookup_ByFullNameDocumentOrCode_ShouldFindThePerson()
    {
        using var client = await CreateSuperAdminClientAsync(await JoinCompanyIdAsync());

        var norma = await LookupAsync(client, "Norma Osorio");
        norma.Select(r => r.GetProperty("fullName").GetString())
            .Should().Contain(["Norma Osorio", "Distribuidora Osorio & Asociados S.R.L."]);

        var byDocument = await LookupAsync(client, "20548796");
        byDocument[0].GetProperty("fullName").GetString().Should().Be("Livingstone Cano");

        var partialDocument = await LookupAsync(client, "2054879");
        partialDocument.Select(r => r.GetProperty("identificationNumber").GetString()).Should().Contain("20548796");

        var byCode = await LookupAsync(client, "c000000001");
        byCode[0].GetProperty("customerCode").GetString().Should().Be("C000000001");
    }

    [Fact]
    public async Task Lookup_ShouldExcludeOtherCompaniesAndInactiveOrDeletedPersons()
    {
        var joinId = await JoinCompanyIdAsync();
        var tag = $"Zq{Guid.NewGuid():N}"[..10];
        var otherCompany = await SeedCompanyAsync();
        await SeedPersonAsync(otherCompany, tag, active: true, deleted: false);
        await SeedPersonAsync(joinId, tag, active: false, deleted: false);
        await SeedPersonAsync(joinId, tag, active: true, deleted: true);
        var visible = await SeedPersonAsync(joinId, tag, active: true, deleted: false);
        using var client = await CreateSuperAdminClientAsync(joinId);

        var rows = await LookupAsync(client, tag);

        rows.Select(r => r.GetProperty("personId").GetGuid()).Should().Equal(visible);
    }

    [Theory]
    [InlineData("")]
    [InlineData("%20c%20")]
    public async Task Lookup_WithAShortTerm_ShouldAnswerInvalidSearchTerm(string term)
    {
        using var client = await CreateSuperAdminClientAsync(await JoinCompanyIdAsync());

        var response = await client.GetAsync($"/api/v1/Persons/lookup?term={term}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("INVALID_SEARCH_TERM");
    }

    [Fact]
    public async Task Lookup_Sql_ShouldExplainWithoutErrors()
    {
        await using var connection = new NpgsqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("EXPLAIN " + GetPersonLookupQueryHandler.Sql
            .Replace("@TenantId", "'00000000-0000-0000-0000-000000000000'::uuid")
            .Replace("@Pattern", "'%cano%'")
            .Replace("@Term", "'cano'")
            .Replace("@Limit", "20"), connection);

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
    }

    // ---------------------------------------------------------------- SPEC 55

    [Fact]
    public async Task CreateCustomer_Twice_ShouldAnswerCreatedThenConfirmedWithTheSameRow()
    {
        var company = await SeedCompanyAsync();
        var personId = await SeedPersonAsync(company, $"Px{Guid.NewGuid():N}"[..10], active: true, deleted: false);
        using var client = await CreateSuperAdminClientAsync(company);

        var first = await client.PostAsJsonAsync("/api/v1/Customers", new { personId });
        var second = await client.PostAsJsonAsync("/api/v1/Customers", new { personId, userId = Guid.NewGuid() });

        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var a = Data(await first.Content.ReadAsStringAsync());
        var b = JsonDocument.Parse(await second.Content.ReadAsStringAsync()).RootElement;
        b.GetProperty("message").GetString().Should().Be("Confirmed");
        b.GetProperty("data").GetProperty("id").GetGuid().Should().Be(a.GetProperty("id").GetGuid());
        b.GetProperty("data").GetProperty("customerCode").GetString().Should().Be(a.GetProperty("customerCode").GetString());
        a.GetProperty("customerStageCode").GetString().Should().Be(CustomerStage.Codes.Customer);
    }

    [Fact]
    public async Task EnsureActive_TenConcurrentCalls_ShouldLeaveOneRowAndOneCreated()
    {
        var company = await SeedCompanyAsync();
        var personId = await SeedPersonAsync(company, $"Pc{Guid.NewGuid():N}"[..10], active: true, deleted: false);
        using var client = await CreateSuperAdminClientAsync(company);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => client.PostAsJsonAsync("/api/v1/Customers", new { personId })));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.OK);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = await db.Customers.IgnoreQueryFilters().Where(c => c.PersonId == personId).ToListAsync();
        rows.Should().ContainSingle();
        var history = await db.CustomerStatusHistory.IgnoreQueryFilters().Where(h => h.CustomerId == rows[0].Id).ToListAsync();
        history.Count(h => h.Action == CustomerStatusAction.Created).Should().Be(1);
        history.Count(h => h.Action == CustomerStatusAction.Confirmed).Should().Be(9);
    }

    [Fact]
    public async Task DeactivateActivateAndHistory_ShouldWorkAndDeleteShouldNotExist()
    {
        var company = await SeedCompanyAsync();
        var personId = await SeedPersonAsync(company, $"Pd{Guid.NewGuid():N}"[..10], active: true, deleted: false);
        using var client = await CreateSuperAdminClientAsync(company);
        var created = Data(await (await client.PostAsJsonAsync("/api/v1/Customers", new { personId })).Content.ReadAsStringAsync());
        var id = created.GetProperty("id").GetGuid();

        var deactivated = await client.PostAsJsonAsync($"/api/v1/Customers/{id}/deactivate", new { notes = "Pausa" });
        deactivated.StatusCode.Should().Be(HttpStatusCode.OK, await deactivated.Content.ReadAsStringAsync());
        Data(await deactivated.Content.ReadAsStringAsync()).GetProperty("isActive").GetBoolean().Should().BeFalse();

        var activated = await client.PostAsync($"/api/v1/Customers/{id}/activate", content: null);
        activated.StatusCode.Should().Be(HttpStatusCode.OK, await activated.Content.ReadAsStringAsync());
        Data(await activated.Content.ReadAsStringAsync()).GetProperty("isActive").GetBoolean().Should().BeTrue();

        var history = await client.GetAsync($"/api/v1/Customers/{id}/status-history");
        history.StatusCode.Should().Be(HttpStatusCode.OK, await history.Content.ReadAsStringAsync());
        var items = Data(await history.Content.ReadAsStringAsync()).GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("actionName").GetString()).Should().Equal("Activated", "Deactivated", "Created");
        items[1].GetProperty("notes").GetString().Should().Be("Pausa");
        items.Should().OnlyContain(i => i.GetProperty("sourceName").GetString() == "Manual");

        var detail = Data(await (await client.GetAsync($"/api/v1/Customers/{id}")).Content.ReadAsStringAsync());
        detail.GetProperty("lastStatusAction").GetInt32().Should().Be((int)CustomerStatusAction.Activated);

        (await client.DeleteAsync($"/api/v1/Customers/{id}")).StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        (await client.PostAsync($"/api/v1/Customers/{id}/restore", content: null)).StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Database_ShouldRejectASecondCustomerForTheSamePerson()
    {
        var company = await SeedCompanyAsync();
        var personId = await SeedPersonAsync(company, $"Pu{Guid.NewGuid():N}"[..10], active: true, deleted: false);
        using var client = await CreateSuperAdminClientAsync(company);
        var created = Data(await (await client.PostAsJsonAsync("/api/v1/Customers", new { personId })).Content.ReadAsStringAsync());

        await using var connection = new NpgsqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var insert = new NpgsqlCommand("""
            INSERT INTO customer.customers (id, companyid, personid, userid, customercode, customerstageid, isactive, activatedat, created, gcrecord)
            SELECT gen_random_uuid(), companyid, personid, userid, 'C999999998', customerstageid, TRUE, NOW(), NOW(), 20261015
            FROM customer.customers WHERE id = @id;
            """, connection);
        insert.Parameters.AddWithValue("id", created.GetProperty("id").GetGuid());

        var act = () => insert.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23505");
        await using var schema = new NpgsqlCommand("SELECT to_regclass('admin.customers') IS NULL AND to_regclass('customer.customers') IS NOT NULL", connection);
        ((bool)(await schema.ExecuteScalarAsync())!).Should().BeTrue("customers moved to the customer schema");
    }

    [Fact]
    public async Task CustomerStages_ShouldEnforceUniquenessPerCompanyAndExposeTheMenuOption()
    {
        var company = await SeedCompanyAsync();
        using var client = await CreateSuperAdminClientAsync(company);

        var list = await client.GetAsync("/api/v1/CustomerStages");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        Data(await list.Content.ReadAsStringAsync()).EnumerateArray().Select(s => s.GetProperty("code").GetString())
            .Should().Equal(CustomerStage.Codes.Lead, CustomerStage.Codes.Prospect, CustomerStage.Codes.Customer, CustomerStage.Codes.Former);

        var duplicate = await client.PostAsJsonAsync("/api/v1/CustomerStages", new { code = "lead", name = "Otro", sortOrder = 9, isActive = true });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var created = await client.PostAsJsonAsync("/api/v1/CustomerStages", new { code = "VIP", name = "VIP", sortOrder = 9, isActive = true });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var stageId = Data(await created.Content.ReadAsStringAsync()).GetProperty("id").GetGuid();
        (await client.DeleteAsync($"/api/v1/CustomerStages/{stageId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/api/v1/CustomerStages/{stageId}/restore", content: null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var option = await db.SystemOptions.IgnoreQueryFilters().SingleAsync(o => o.Name == "CustomerStages");
        option.Route.Should().Be("/customers/customer-stages");
        option.ControllerName.Should().Be("CustomerStages");
    }

    // ---------------------------------------------------------------- helpers

    private static JsonElement Data(string body) => JsonDocument.Parse(body).RootElement.GetProperty("data");

    private static async Task<List<JsonElement>> LookupAsync(HttpClient client, string term)
    {
        var response = await client.GetAsync($"/api/v1/Persons/lookup?term={Uri.EscapeDataString(term)}&limit=50");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return Data(body).EnumerateArray().ToList();
    }

    private async Task<Guid> JoinCompanyIdAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await JoinCompanyIdAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private static Task<Guid> JoinCompanyIdAsync(ApplicationDbContext db) =>
        db.Companies.IgnoreQueryFilters().Where(c => c.TaxId == "JOIN-001").Select(c => c.Id).SingleAsync();

    /// <summary>
    /// A fresh company with the four initial customer stages (as <c>POST /Companies</c> would create them).
    /// </summary>
    private async Task<Guid> SeedCompanyAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var company = new Company
        {
            Name = $"S55-{Guid.NewGuid():N}"[..20],
            TaxId = $"S55{Guid.NewGuid():N}"[..16],
            IsActive = true,
            CreatedBy = Creator
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<JOIN.Persistence.DatabaseSeeder>().SeedCustomerStagesForCompanyAsync(company.Id);
        return company.Id;
    }

    private async Task<Guid> SeedPersonAsync(Guid companyId, string lastName, bool active, bool deleted)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var idType = await db.IdentificationTypes.IgnoreQueryFilters().Where(t => t.Name == "Cedula").Select(t => t.Id).SingleAsync();
        var person = new Person
        {
            CompanyId = companyId,
            PersonType = PersonType.Physical,
            FirstName = "Test",
            LastName = lastName,
            IdentificationTypeId = idType,
            IdentificationNumber = $"T{Guid.NewGuid():N}"[..18],
            CreatedBy = Creator
        };
        if (!active)
        {
            person.Deactivate();
        }

        if (deleted)
        {
            person.MarkAsDeleted();
        }

        db.Persons.Add(person);
        await db.SaveChangesAsync();
        return person.Id;
    }

    private async Task<HttpClient> CreateSuperAdminClientAsync(Guid companyId)
    {
        var email = $"s55{Guid.NewGuid():N}@t.local";
        using var unauthClient = _factory.CreateClient();
        (await unauthClient.PostAsJsonAsync("/api/v1/users/register", new RegisterCommand
        {
            Email = email,
            Password = Password,
            FirstName = "Spec55",
            LastName = "Admin"
        })).EnsureSuccessStatusCode();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            db.UserCompanies.Add(new UserCompany { UserId = user!.Id, CompanyId = companyId, IsDefault = true, CreatedBy = Creator });
            (await userManager.AddToRoleAsync(user, "SuperAdmin")).Succeeded.Should().BeTrue();
            await db.SaveChangesAsync();
        }

        var login = await unauthClient.PostAsJsonAsync("/api/v1/users/login", new LoginCommand
        {
            Email = email,
            Password = Password,
            TargetCompanyId = companyId
        });
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<Response<LoginResponse>>();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Data!.Token);
        return client;
    }
}
