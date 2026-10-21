// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static JOIN.IntegrationTests.Customers.CommercialTestHelpers;

namespace JOIN.IntegrationTests.Common;

/// <summary>
/// SPEC 71 — the email templates of the company against PostgreSQL 17: the seed of the four types, the
/// closed list of variables, the sanitized body, the unique name per company, the lock by use of the
/// types, the tenant isolation, the lookup of the sending modal and the <c>EXPLAIN</c> of the new SQL.
/// </summary>
public sealed class EmailTemplatesPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public EmailTemplatesPostgreSqlTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static async Task<Dictionary<string, Guid>> TypeIdsAsync(HttpClient client)
    {
        var types = (await DataAsync(await client.GetAsync("/api/v1/EmailTemplateTypes"))).EnumerateArray().ToList();
        return types.ToDictionary(t => t.GetProperty("code").GetString()!, t => t.GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> NewTemplateAsync(
        HttpClient client,
        Guid typeId,
        string name,
        string subject = "Hola {{Persona.Nombre}}",
        string body = "<p>Hola {{Persona.Nombre}}</p>",
        bool isActive = true)
    {
        var response = await client.PostAsJsonAsync("/api/v1/EmailTemplates", new
        {
            emailTemplateTypeId = typeId,
            name,
            subject,
            bodyHtml = body,
            isActive
        });
        var body2 = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, body2);
        return await DataAsync(response);
    }

    private static async Task<Guid> NewPersonAsync(HttpClient client, string firstName, string lastName)
    {
        // Legal person: the natural one needs a gender id, and the commercial name is left empty
        // so {{Persona.Nombre}} resolves to the first name.
        var response = await client.PostAsJsonAsync("/api/v1/Persons", new { personType = 2, firstName, lastName });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").GetGuid();
    }

    private static async Task<Guid> NewLeadAsync(HttpClient client, Guid personId, string topic)
    {
        var catalogs = await DataAsync(await client.GetAsync("/api/v1/Leads/catalogs"));
        var relationshipTypeId = catalogs.GetProperty("relationshipTypes").EnumerateArray()
            .Single(r => r.GetProperty("code").GetString() == "PROSPECT_CUSTOMER").GetProperty("id").GetGuid();
        var leadSourceId = catalogs.GetProperty("sources").EnumerateArray()
            .Single(s => s.GetProperty("code").GetString() == "WEB").GetProperty("id").GetGuid();
        var response = await client.PostAsJsonAsync("/api/v1/Leads", new { personId, topic, relationshipTypeId, leadSourceId });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return (await DataAsync(response)).GetProperty("lead").GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task EveryNewCompany_IsBornWithTheFourSeedTypes_AndTheSeedIsIdempotent()
    {
        var (companyId, client, _) = await CreateCompanyWithUserAsync(_factory);

        var types = (await DataAsync(await client.GetAsync("/api/v1/EmailTemplateTypes"))).EnumerateArray().ToList();
        types.Select(t => t.GetProperty("code").GetString()).Should().Equal("INTRO", "PROPOSAL", "FOLLOW_UP", "THANKS");
        types.Select(t => t.GetProperty("name").GetString()).Should().Equal("Presentación", "Propuesta", "Seguimiento", "Agradecimiento");
        types.Select(t => t.GetProperty("sortOrder").GetInt32()).Should().Equal(1, 2, 3, 4);
        types.Should().OnlyContain(t => t.GetProperty("isActive").GetBoolean());

        // Idempotent: seeding again does not duplicate the rows.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICompanyCatalogSeeder>().SeedDefaultCatalogsForCompanyAsync(companyId);
        }

        await using var check = _factory.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.EmailTemplateTypes.IgnoreQueryFilters().CountAsync(t => t.CompanyId == companyId && t.GcRecord == 0)).Should().Be(4);
    }

    [Fact]
    public async Task Create_RejectsTheUnknownVariable_AndStoresTheBodySanitized()
    {
        var (_, client, _) = await CreateCompanyWithUserAsync(_factory);
        var typeId = (await TypeIdsAsync(client))["INTRO"];

        var unknown = await client.PostAsJsonAsync("/api/v1/EmailTemplates", new
        {
            emailTemplateTypeId = typeId,
            name = "Con variable mala",
            subject = "Hola {{Persona.Apodo}}",
            bodyHtml = "<p>{{Lead.Invitado}}</p>"
        });
        var body = await unknown.Content.ReadAsStringAsync();
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        body.Should().Contain("EMAIL_TEMPLATE_UNKNOWN_VARIABLE");
        body.Should().Contain("Persona.Apodo");
        body.Should().Contain("Lead.Invitado");

        var created = await NewTemplateAsync(
            client,
            typeId,
            "Bienvenida",
            "Hola {{Persona.Nombre}}",
            "<p onclick=\"robar()\">Hola {{Persona.Nombre}}</p><script>alert(1)</script><a href=\"javascript:robar()\">x</a>");
        created.GetProperty("name").GetString().Should().Be("Bienvenida");
        created.GetProperty("emailTemplateTypeCode").GetString().Should().Be("INTRO");
        var bodyHtml = created.GetProperty("bodyHtml").GetString()!;
        bodyHtml.Should().NotContain("<script").And.NotContain("onclick").And.NotContain("javascript:");
        bodyHtml.Should().Contain("{{Persona.Nombre}}");

        // The stored body is the sanitized one, not the raw payload.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.EmailTemplates.IgnoreQueryFilters()
            .SingleAsync(t => t.Id == created.GetProperty("id").GetGuid());
        stored.BodyHtml.Should().NotContain("<script").And.NotContain("onclick");
    }

    [Fact]
    public async Task TheNameIsUniquePerCompany_ButTwoCompaniesMayShareIt()
    {
        var (_, first, _) = await CreateCompanyWithUserAsync(_factory);
        var (_, second, _) = await CreateCompanyWithUserAsync(_factory);
        var firstType = (await TypeIdsAsync(first))["INTRO"];
        var secondType = (await TypeIdsAsync(second))["INTRO"];

        await NewTemplateAsync(first, firstType, "Comercial");
        await NewTemplateAsync(second, secondType, "Comercial");

        var duplicated = await first.PostAsJsonAsync("/api/v1/EmailTemplates", new
        {
            emailTemplateTypeId = firstType,
            name = "Comercial",
            subject = "Hola",
            bodyHtml = "<p>Hola</p>"
        });
        var body = await duplicated.Content.ReadAsStringAsync();
        duplicated.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        body.Should().Contain("DUPLICATE_KEY");

        // The filtered unique index blocks the race even when the application check is bypassed.
        var companyId = (await DataAsync(await first.GetAsync("/api/v1/EmailTemplateTypes")))
            .EnumerateArray().First().GetProperty("companyId").GetGuid();
        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO admin.emailtemplates (id, companyid, emailtemplatetypeid, name, subject, bodyhtml, isactive, gcrecord, created)
            SELECT gen_random_uuid(), @company, t.id, 'Comercial', 'Hola', '<p>Hola</p>', TRUE, 0, NOW()
            FROM admin.emailtemplatetypes t WHERE t.companyid = @company AND t.code = 'INTRO' AND t.gcrecord = 0
            """, conn);
        insert.Parameters.AddWithValue("company", companyId);
        var act = () => insert.ExecuteNonQueryAsync();
        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23505");
    }

    [Fact]
    public async Task DeletingATypeWithLiveTemplates_Answers409()
    {
        var (_, client, _) = await CreateCompanyWithUserAsync(_factory);
        var typeId = (await TypeIdsAsync(client))["INTRO"];
        await NewTemplateAsync(client, typeId, "Bienvenida");

        var blocked = await client.DeleteAsync($"/api/v1/EmailTemplateTypes/{typeId}");
        var body = await blocked.Content.ReadAsStringAsync();
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        body.Should().Contain("EMAIL_TEMPLATE_TYPE_IN_USE");
        body.Should().Contain("Active EmailTemplates: 1");

        // With the template deleted the type can be deleted too.
        var templates = (await DataAsync(await client.GetAsync("/api/v1/EmailTemplates"))).GetProperty("items").EnumerateArray().ToList();
        (await client.DeleteAsync($"/api/v1/EmailTemplates/{templates[0].GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync($"/api/v1/EmailTemplateTypes/{typeId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Lookup_ReturnsOnlyTheActiveTemplates_AndOpensWithoutTheAdminPermission()
    {
        var (companyId, client, _) = await CreateCompanyWithUserAsync(_factory);
        var typeId = (await TypeIdsAsync(client))["INTRO"];
        await NewTemplateAsync(client, typeId, "Activa");
        await NewTemplateAsync(client, typeId, "Inactiva", isActive: false);

        var lookup = (await DataAsync(await client.GetAsync("/api/v1/EmailTemplates/lookup"))).EnumerateArray().ToList();
        lookup.Select(t => t.GetProperty("name").GetString()).Should().Equal("Activa");
        lookup[0].GetProperty("emailTemplateTypeCode").GetString().Should().Be("INTRO");

        // A user without the EmailTemplates permission can still use the picker of the sending modal.
        var (seller, _) = await AddUserAsync(_factory, companyId, "UsuarioSimple");
        (await seller.GetAsync("/api/v1/EmailTemplates")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var sellerLookup = (await DataAsync(await seller.GetAsync("/api/v1/EmailTemplates/lookup"))).EnumerateArray().ToList();
        sellerLookup.Select(t => t.GetProperty("name").GetString()).Should().Equal("Activa");
        (await DataAsync(await seller.GetAsync("/api/v1/EmailTemplates/variables"))).GetArrayLength().Should().Be(10);
    }

    [Fact]
    public async Task Render_ReplacesTheVariables_AndLeavesTheLeadOnesEmpty()
    {
        var (_, client, _) = await CreateCompanyWithUserAsync(_factory);
        var typeId = (await TypeIdsAsync(client))["INTRO"];
        var template = await NewTemplateAsync(
            client,
            typeId,
            "Bienvenida",
            "Cliente [{{Cliente.Codigo}}] lead [{{Lead.Codigo}}] [{{Lead.Tema}}]",
            "<p>{{Persona.Nombre}} / {{Persona.NombreCompleto}} / {{Empresa.Nombre}} / {{Fecha.Hoy}}</p>");
        var templateId = template.GetProperty("id").GetGuid();
        var personId = await NewPersonAsync(client, "Ana", "López");

        var rendered = await DataAsync(await client.PostAsJsonAsync($"/api/v1/EmailTemplates/{templateId}/render", new { personId }));
        rendered.GetProperty("subject").GetString().Should().Be("Cliente [] lead [] []");
        var bodyHtml = rendered.GetProperty("bodyHtml").GetString()!;
        bodyHtml.Should().StartWith("<p>Ana / Ana López / COM-");
        bodyHtml.Should().NotContain("{{");

        // The date of the render is the today of the server.
        var today = DateTime.Now.ToString("dd/MM/yyyy");
        bodyHtml.Should().Contain(today);
    }

    [Fact]
    public async Task Render_WithALeadOfAnotherPerson_Answers404()
    {
        var (_, client, _) = await CreateCompanyWithUserAsync(_factory);
        var typeId = (await TypeIdsAsync(client))["INTRO"];
        var template = await NewTemplateAsync(client, typeId, "Bienvenida");
        var templateId = template.GetProperty("id").GetGuid();

        var personId = await NewPersonAsync(client, "Ana", "López");
        var otherId = await NewPersonAsync(client, "Luis", "Pérez");
        var leadId = await NewLeadAsync(client, otherId, "Renovación");

        var rendered = await client.PostAsJsonAsync($"/api/v1/EmailTemplates/{templateId}/render", new { personId, leadId });
        var body = await rendered.Content.ReadAsStringAsync();
        rendered.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
        body.Should().Contain("LEAD_NOT_FOUND");

        // The lead of the same person renders its variables.
        var ownLeadId = await NewLeadAsync(client, personId, "Propuesta");
        var ok = await DataAsync(await client.PostAsJsonAsync($"/api/v1/EmailTemplates/{templateId}/render", new { personId, leadId = ownLeadId }));
        ok.GetProperty("id").GetGuid().Should().Be(templateId);
    }

    [Fact]
    public async Task DeletingATemplate_IsLogical_AndTheRestoreIsSuperAdminOnly()
    {
        var (companyId, client, _) = await CreateCompanyWithUserAsync(_factory);
        var typeId = (await TypeIdsAsync(client))["INTRO"];
        var template = await NewTemplateAsync(client, typeId, "Bienvenida");
        var templateId = template.GetProperty("id").GetGuid();

        (await client.DeleteAsync($"/api/v1/EmailTemplates/{templateId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/v1/EmailTemplates/{templateId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.EmailTemplates.IgnoreQueryFilters().SingleAsync(t => t.Id == templateId);
            stored.GcRecord.Should().NotBe(0);
        }

        var (seller, _) = await AddUserAsync(_factory, companyId, "UsuarioSimple");
        (await seller.PostAsync($"/api/v1/EmailTemplates/{templateId}/restore", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync($"/api/v1/EmailTemplates/{templateId}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/v1/EmailTemplates/{templateId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheTemplatesOfAnotherCompanyAreNotVisible()
    {
        var (_, first, _) = await CreateCompanyWithUserAsync(_factory);
        var (_, second, _) = await CreateCompanyWithUserAsync(_factory);
        var firstType = (await TypeIdsAsync(first))["INTRO"];
        var template = await NewTemplateAsync(first, firstType, "Privada");
        var templateId = template.GetProperty("id").GetGuid();

        (await second.GetAsync($"/api/v1/EmailTemplates/{templateId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await second.DeleteAsync($"/api/v1/EmailTemplates/{templateId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var list = (await DataAsync(await second.GetAsync("/api/v1/EmailTemplates"))).GetProperty("items").EnumerateArray().ToList();
        list.Should().NotContain(t => t.GetProperty("id").GetGuid() == templateId);
        var lookup = (await DataAsync(await second.GetAsync("/api/v1/EmailTemplates/lookup"))).EnumerateArray().ToList();
        lookup.Should().NotContain(t => t.GetProperty("id").GetGuid() == templateId);
    }

    [Fact]
    public async Task Render_UsesTheCustomerCodeThePrimaryEmailAndTheSender()
    {
        var (companyId, client, userId) = await CreateCompanyWithUserAsync(_factory);
        var typeId = (await TypeIdsAsync(client))["INTRO"];
        var templateId = (await NewTemplateAsync(
            client, typeId, "Datos", "{{Cliente.Codigo}}", "<p>{{Persona.Correo}}|{{Usuario.Correo}}|{{Empresa.Nombre}}</p>"))
            .GetProperty("id").GetGuid();
        var personId = await NewPersonAsync(client, "Ana", "López");
        string userEmail;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stageId = await db.CustomerStages.IgnoreQueryFilters().Where(st => st.CompanyId == companyId && st.GcRecord == 0).Select(st => st.Id).FirstAsync();
            db.Customers.Add(JOIN.Domain.Customers.Customer.Create(companyId, personId, userId, "C-777", stageId));
            // The alternative e-mail goes first in the table: the render must still prefer the primary one.
            db.PersonContacts.Add(JOIN.Domain.Admin.PersonContact.Create(companyId, personId, JOIN.Domain.Enums.ContactType.AlternativeEmail, "alterno@acme.test"));
            db.PersonContacts.Add(JOIN.Domain.Admin.PersonContact.Create(companyId, personId, JOIN.Domain.Enums.ContactType.PrimaryEmail, "principal@acme.test"));
            await db.SaveChangesAsync();
            userEmail = await db.Users.Where(u => u.Id == userId).Select(u => u.Email!).SingleAsync();
        }

        var rendered = await DataAsync(await client.PostAsJsonAsync($"/api/v1/EmailTemplates/{templateId}/render", new { personId }));

        rendered.GetProperty("subject").GetString().Should().Be("C-777");
        rendered.GetProperty("bodyHtml").GetString().Should().StartWith($"<p>principal@acme.test|{userEmail}|COM-");
        var chosen = await DataAsync(await client.PostAsJsonAsync($"/api/v1/EmailTemplates/{templateId}/render", new { personId, emailAddress = "alterno@acme.test" }));
        chosen.GetProperty("bodyHtml").GetString().Should().StartWith("<p>alterno@acme.test|");
    }

    [Fact]
    public async Task DeletedRows_AreVisibleOnlyToTheSuperAdmin_AndATemplateIsNotRestoredWhileItsTypeIsDeleted()
    {
        var (companyId, client, _) = await CreateCompanyWithUserAsync(_factory);
        var typeResponse = await client.PostAsJsonAsync("/api/v1/EmailTemplateTypes", new { code = "NEWS", name = "Novedades", sortOrder = 9 });
        var typeId = (await DataAsync(typeResponse)).GetProperty("id").GetGuid();
        var templateId = (await NewTemplateAsync(client, typeId, "Boletín")).GetProperty("id").GetGuid();
        (await client.DeleteAsync($"/api/v1/EmailTemplates/{templateId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync($"/api/v1/EmailTemplateTypes/{typeId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var withDeleted = (await DataAsync(await client.GetAsync("/api/v1/EmailTemplates?includeDeleted=true"))).GetProperty("items").EnumerateArray().ToList();
        withDeleted.Single(t => t.GetProperty("id").GetGuid() == templateId).GetProperty("isDeleted").GetBoolean().Should().BeTrue();
        (await DataAsync(await client.GetAsync("/api/v1/EmailTemplateTypes?includeDeleted=true"))).EnumerateArray()
            .Should().Contain(t => t.GetProperty("id").GetGuid() == typeId);

        var (supervisor, _) = await AddUserAsync(_factory, companyId, "Supervisor");
        await GrantAsync(_factory, companyId, "Supervisor", "EmailTemplates", create: false, update: false, delete: false);
        (await DataAsync(await supervisor.GetAsync("/api/v1/EmailTemplates?includeDeleted=true"))).GetProperty("items").EnumerateArray()
            .Should().NotContain(t => t.GetProperty("id").GetGuid() == templateId);

        var restore = await client.PostAsync($"/api/v1/EmailTemplates/{templateId}/restore", null);
        restore.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await restore.Content.ReadAsStringAsync()).Should().Contain("PARENT_DELETED");
        (await client.PostAsync($"/api/v1/EmailTemplateTypes/{typeId}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/api/v1/EmailTemplates/{templateId}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_KeepsAnInactiveType_ButCannotAssignOne()
    {
        var (_, client, _) = await CreateCompanyWithUserAsync(_factory);
        var types = await TypeIdsAsync(client);
        var templateId = (await NewTemplateAsync(client, types["INTRO"], "Intro")).GetProperty("id").GetGuid();
        (await client.PutAsJsonAsync($"/api/v1/EmailTemplateTypes/{types["INTRO"]}", new { code = "INTRO", name = "Presentación", sortOrder = 1, isActive = false }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync($"/api/v1/EmailTemplateTypes/{types["THANKS"]}", new { code = "THANKS", name = "Agradecimiento", sortOrder = 4, isActive = false }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PutAsJsonAsync($"/api/v1/EmailTemplates/{templateId}", new { emailTemplateTypeId = types["INTRO"], name = "Intro", subject = "Hola", bodyHtml = "<p>Hola</p>", isActive = true }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync($"/api/v1/EmailTemplates/{templateId}", new { emailTemplateTypeId = types["THANKS"], name = "Intro", subject = "Hola", bodyHtml = "<p>Hola</p>", isActive = true }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task NewSql_ShouldPlanAgainstTheMigratedSchema()
    {
        var (companyId, client, _) = await CreateCompanyWithUserAsync(_factory);
        var typeId = (await TypeIdsAsync(client))["INTRO"];
        var templateId = (await NewTemplateAsync(client, typeId, "Bienvenida")).GetProperty("id").GetGuid();

        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();

        (await ExplainAsync(conn, """
            SELECT Id, CompanyId, Code, Name, SortOrder, IsActive, Created AS CreatedAt
            FROM admin.emailtemplatetypes
            WHERE CompanyId = @CompanyId
              AND GcRecord = 0
              AND (@IncludeInactive = TRUE OR IsActive = TRUE)
            ORDER BY SortOrder, Name;
            """, companyId, ("IncludeInactive", true))).Should().Contain("Plan");

        (await ExplainAsync(conn, """
            SELECT
                t.Id, t.CompanyId, t.EmailTemplateTypeId,
                tt.Name AS EmailTemplateTypeName, tt.Code AS EmailTemplateTypeCode,
                t.Name, t.Subject, t.BodyHtml, t.IsActive, t.Created AS CreatedAt
            FROM admin.emailtemplates t
            JOIN admin.emailtemplatetypes tt ON tt.Id = t.EmailTemplateTypeId
            WHERE t.CompanyId = @CompanyId AND t.GcRecord = 0
              AND (t.Name ILIKE @Search OR t.Subject ILIKE @Search)
              AND t.EmailTemplateTypeId = @EmailTemplateTypeId
              AND t.IsActive = @IsActive
            ORDER BY t.Name
            LIMIT @PageSize OFFSET @Offset;
            """, companyId, ("PageSize", 20), ("Offset", 0), ("Search", "%Bien%"), ("EmailTemplateTypeId", typeId), ("IsActive", true))).Should().Contain("Plan");

        (await ExplainAsync(conn, """
            SELECT COUNT(*)
            FROM admin.emailtemplates t
            WHERE t.CompanyId = @CompanyId AND t.GcRecord = 0;
            """, companyId)).Should().Contain("Plan");

        (await ExplainAsync(conn, """
            SELECT
                t.Id, t.CompanyId, t.EmailTemplateTypeId,
                tt.Name AS EmailTemplateTypeName, tt.Code AS EmailTemplateTypeCode,
                t.Name, t.Subject, t.BodyHtml, t.IsActive, t.Created AS CreatedAt
            FROM admin.emailtemplates t
            JOIN admin.emailtemplatetypes tt ON tt.Id = t.EmailTemplateTypeId
            WHERE t.Id = @Id AND t.CompanyId = @CompanyId AND t.GcRecord = 0;
            """, companyId, ("Id", templateId))).Should().Contain("Plan");

        (await ExplainAsync(conn, """
            SELECT
                t.Id, t.Name, t.Subject, t.EmailTemplateTypeId,
                tt.Name AS EmailTemplateTypeName, tt.Code AS EmailTemplateTypeCode
            FROM admin.emailtemplates t
            JOIN admin.emailtemplatetypes tt ON tt.Id = t.EmailTemplateTypeId
            WHERE t.CompanyId = @CompanyId AND t.GcRecord = 0 AND t.IsActive = TRUE
              AND t.EmailTemplateTypeId = @EmailTemplateTypeId
              AND (t.Name ILIKE @Search OR t.Subject ILIKE @Search)
            ORDER BY tt.SortOrder, tt.Name, t.Name
            LIMIT @MaxResults;
            """, companyId, ("MaxResults", 50), ("EmailTemplateTypeId", typeId), ("Search", "%Bien%"))).Should().Contain("Plan");

        // SPEC 71 B.8 — the render reads the active template and the lead of the person.
        (await ExplainAsync(conn, """
            SELECT t.Id, t.Name, t.Subject, t.BodyHtml
            FROM admin.emailtemplates t
            WHERE t.Id = @TemplateId AND t.CompanyId = @CompanyId AND t.GcRecord = 0 AND t.IsActive = TRUE;
            """, companyId, ("TemplateId", Guid.NewGuid()))).Should().Contain("Plan");
        (await ExplainAsync(conn, """
            SELECT l.Code, l.Topic
            FROM customer.leads l
            WHERE l.Id = @LeadId AND l.CompanyId = @CompanyId AND l.PersonId = @PersonId AND l.GcRecord = 0;
            """, companyId, ("LeadId", Guid.NewGuid()), ("PersonId", Guid.NewGuid()))).Should().Contain("Plan");

        // SPEC 71 B.8 — the render reads the person context in one statement.
        (await ExplainAsync(conn, """
            SELECT p.FirstName,
                (SELECT c.CustomerCode FROM customer.customers c WHERE c.CompanyId = @CompanyId AND c.PersonId = p.Id AND c.GcRecord = 0 AND c.IsActive = TRUE LIMIT 1),
                (SELECT pc.ContactValue FROM admin.personcontacts pc WHERE pc.CompanyId = @CompanyId AND pc.PersonId = p.Id AND pc.GcRecord = 0 AND pc.IsActive = TRUE
                   AND pc.ContactType IN (1, 2) ORDER BY CASE WHEN pc.ContactType = 1 THEN 0 ELSE 1 END, pc.IsPrimary DESC LIMIT 1),
                (SELECT cc.TimeZone FROM calendar.calendarcompanies cc WHERE cc.CompanyId = @CompanyId AND cc.GcRecord = 0 LIMIT 1)
            FROM admin.persons p
            WHERE p.Id = @PersonId AND p.CompanyId = @CompanyId AND p.GcRecord = 0;
            """, companyId, ("PersonId", Guid.NewGuid()))).Should().Contain("Plan");
    }

    private static async Task<string?> ExplainAsync(
        NpgsqlConnection connection,
        string sql,
        Guid companyId,
        params (string Name, object Value)[] extra)
    {
        await using var command = new NpgsqlCommand($"EXPLAIN (FORMAT JSON) {sql}", connection);
        command.Parameters.AddWithValue("CompanyId", companyId);
        foreach (var (name, value) in extra)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (string?)await command.ExecuteScalarAsync();
    }
}
