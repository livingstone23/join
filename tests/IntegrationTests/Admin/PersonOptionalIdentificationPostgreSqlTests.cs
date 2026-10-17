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

namespace JOIN.IntegrationTests.Admin;

/// <summary>
/// SPEC 58 — persons without identification against PostgreSQL 17: created, listed and looked up without losing
/// them in a join; identification unique when present; required to become a customer.
/// </summary>
public sealed class PersonOptionalIdentificationPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PersonOptionalIdentificationPostgreSqlTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task APersonWithoutIdentification_IsCreatedListedAndLookedUp_ButOnlyHalfAnIdentificationIsRejected()
    {
        using var client = await ModuleTestHelpers.LoginAsync(_factory, "lcano@join.com");
        var name = $"SinDoc{Guid.NewGuid():N}"[..14];

        var created = await client.PostAsJsonAsync("/api/v1/Persons", new { personType = 2, firstName = name, lastName = "SRL", commercialName = $"{name} SRL" });
        var body = await created.Content.ReadAsStringAsync();
        created.StatusCode.Should().Be(HttpStatusCode.Created, body);
        var personId = JsonDocument.Parse(body).RootElement.GetProperty("data").GetGuid();

        var halfOnlyNumber = await client.PostAsJsonAsync("/api/v1/Persons", new { personType = 2, firstName = "X", lastName = "Y", commercialName = "X Y", identificationNumber = "123" });
        halfOnlyNumber.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await client.GetAsync($"/api/v1/Persons/{personId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await client.GetAsync($"/api/v1/Persons?pageSize=50&firstName={name}");
        var listBody = await list.Content.ReadAsStringAsync();
        list.StatusCode.Should().Be(HttpStatusCode.OK, listBody);
        listBody.Should().Contain(personId.ToString());
        (await client.GetStringAsync($"/api/v1/Persons/lookup?term={name}")).Should().Contain(personId.ToString());
    }

    [Fact]
    public async Task TwoPersonsWithoutIdentification_Coexist_ButARepeatedIdentificationIsRejected()
    {
        using var client = await ModuleTestHelpers.LoginAsync(_factory, "lcano@join.com");
        var typeId = await IdentificationTypeIdAsync();
        var number = $"SPEC58-{Guid.NewGuid():N}"[..20];

        (await client.PostAsJsonAsync("/api/v1/Persons", new { personType = 2, firstName = "Uno", lastName = "SRL", commercialName = "Uno SRL" })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/api/v1/Persons", new { personType = 2, firstName = "Dos", lastName = "SRL", commercialName = "Dos SRL" })).StatusCode.Should().Be(HttpStatusCode.Created);

        (await client.PostAsJsonAsync("/api/v1/Persons", new { personType = 2, firstName = "Tres", lastName = "SRL", commercialName = "Tres SRL", identificationTypeId = typeId, identificationNumber = number }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/api/v1/Persons", new { personType = 2, firstName = "Cuatro", lastName = "SRL", commercialName = "Cuatro SRL", identificationTypeId = typeId, identificationNumber = number }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ACustomer_NeedsTheIdentification()
    {
        using var client = await ModuleTestHelpers.LoginAsync(_factory, "lcano@join.com");
        var created = await client.PostAsJsonAsync("/api/v1/Persons", new { personType = 2, firstName = "Prospecto", lastName = "SRL", commercialName = "Prospecto SRL" });
        var personId = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetGuid();

        var blocked = await client.PostAsJsonAsync("/api/v1/Customers", new { personId });
        blocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await blocked.Content.ReadAsStringAsync()).Should().Contain("PERSON_IDENTIFICATION_REQUIRED");

        await using (var conn = new NpgsqlConnection(_factory.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("UPDATE admin.persons SET identificationtypeid = @type, identificationnumber = @number WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("type", await IdentificationTypeIdAsync());
            cmd.Parameters.AddWithValue("number", $"S58C-{Guid.NewGuid():N}"[..20]);
            cmd.Parameters.AddWithValue("id", personId);
            await cmd.ExecuteNonQueryAsync();
        }

        var allowed = await client.PostAsJsonAsync("/api/v1/Customers", new { personId });
        allowed.StatusCode.Should().Be(HttpStatusCode.Created, await allowed.Content.ReadAsStringAsync());
        var customerId = JsonDocument.Parse(await allowed.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("id").GetGuid();
        (await client.GetAsync($"/api/v1/Customers/{customerId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ThePersonQueries_KeepPersonsWithoutIdentification_InTheirPlans()
    {
        Guid companyId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            companyId = await db.Companies.IgnoreQueryFilters().Where(c => c.TaxId == "JOIN-001").Select(c => c.Id).SingleAsync();
        }

        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            EXPLAIN (FORMAT JSON)
            SELECT p.Id, it.Name, p.IdentificationNumber
            FROM admin.persons p
            LEFT JOIN admin.identificationtypes it ON p.IdentificationTypeId = it.Id
            WHERE p.CompanyId = @company AND p.GcRecord = 0
            """, conn);
        cmd.Parameters.AddWithValue("company", companyId);

        var plan = (string?)await cmd.ExecuteScalarAsync();

        plan.Should().Contain("persons").And.Contain("Left");
    }

    private async Task<Guid> IdentificationTypeIdAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.IdentificationTypes.IgnoreQueryFilters().Where(t => t.GcRecord == 0).Select(t => t.Id).FirstAsync();
    }
}
