// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JOIN.Application.Interface.Email;
using JOIN.Domain.Admin;
using JOIN.Domain.Customers.Leads;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static JOIN.IntegrationTests.Customers.CommercialTestHelpers;
using PersonContactType = JOIN.Domain.Enums.ContactType;

namespace JOIN.IntegrationTests.Common;

/// <summary>
/// SPEC 72 — sending emails to persons, customers and leads against PostgreSQL 17, with <see cref="CapturingEmailService"/>
/// as the provider: valid destinations, the lead contact and timeline, open leads only, the <c>Failed</c> path, sender and
/// Reply-To, rendered variables, attachments, the daily quota, the <c>CanExecute</c> permission and the <c>EXPLAIN</c>
/// of the new SQL.
/// </summary>
public sealed class PersonEmailsPostgreSqlTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private CapturingEmailService Capture => factory.Services.GetRequiredService<CapturingEmailService>();

    [Fact]
    public async Task AddressesAreTheValidOnes_AndAPersonWithoutThemCannotReceive()
    {
        var (companyId, client, _) = await CreateCompanyWithUserAsync(factory);
        var personId = await NewPersonAsync(client, companyId, ("no-es-correo", PersonContactType.PrimaryEmail, true));

        (await DataAsync(await client.GetAsync($"/api/v1/Persons/{personId}/email-addresses"))).GetArrayLength().Should().Be(0);
        await ExpectAsync(client.PostAsJsonAsync($"/api/v1/Persons/{personId}/emails", Mail()), HttpStatusCode.BadRequest, "PERSON_EMAIL_NOT_AVAILABLE");

        var withMail = await NewPersonAsync(client, companyId,
            ("mal@formado", PersonContactType.AlternativeEmail, false), ($"{Guid.NewGuid():N}@cliente.test", PersonContactType.PrimaryEmail, true));
        var addresses = (await DataAsync(await client.GetAsync($"/api/v1/Persons/{withMail}/email-addresses"))).EnumerateArray().ToList();
        addresses.Should().ContainSingle().Which.GetProperty("isPrimary").GetBoolean().Should().BeTrue();
        await ExpectAsync(client.PostAsJsonAsync($"/api/v1/Persons/{withMail}/emails", Mail(to: "ajeno@externo.test")), HttpStatusCode.BadRequest, "INVALID_EMAIL_ADDRESS");
    }

    [Fact]
    public async Task FromTheLead_SavesSent_CreatesTheEmailContact_AndTheTimelineLinksIt()
    {
        var (companyId, client, userId) = await CreateCompanyWithUserAsync(factory);
        var email = $"{Guid.NewGuid():N}@cliente.test";
        var personId = await NewPersonAsync(client, companyId, (email, PersonContactType.PrimaryEmail, true));
        var leadId = await NewLeadAsync(client, personId);

        var sent = await DataAsync(await client.PostAsJsonAsync($"/api/v1/Leads/{leadId}/emails", Mail("Hola {{Persona.Nombre}} — {{Lead.Tema}}")));

        var messageId = sent.GetProperty("id").GetGuid();
        sent.GetProperty("status").GetInt32().Should().Be(1);
        var captured = Capture.LastMessageTo(email)!;
        captured.Subject.Should().Be("Hola Ana — Renovación");
        captured.ReplyTo.Should().Be(await UserEmailAsync(userId));
        captured.FromEmail.Should().Be(factory.Services.GetRequiredService<IEmailSenderDefaults>().FromEmail, "the company has no sender of its own");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var message = await db.PersonEmailMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == messageId);
            message.Status.Should().Be(PersonEmailStatus.Sent);
            message.Origin.Should().Be(PersonEmailOrigin.Lead);
            var interaction = await db.LeadInteractions.IgnoreQueryFilters().SingleAsync(i => i.PersonEmailMessageId == messageId);
            interaction.Summary.Should().StartWith("Correo: Hola Ana — Renovación");
            (await db.Leads.IgnoreQueryFilters().SingleAsync(l => l.Id == leadId)).LastInteractionAt.Should().BeCloseTo(message.SentAt, TimeSpan.FromMilliseconds(1));
        }

        var timeline = (await DataAsync(await client.GetAsync($"/api/v1/Leads/{leadId}/timeline"))).EnumerateArray().ToList();
        var item = timeline.Single(t => t.GetProperty("kind").GetString() == "Interaction");
        item.GetProperty("emailMessageId").GetGuid().Should().Be(messageId);
        item.GetProperty("contactTypeCode").GetString().Should().Be("EMAIL");

        (await DataAsync(await client.GetAsync($"/api/v1/Leads/{leadId}/emails"))).GetProperty("totalCount").GetInt32().Should().Be(1);
        (await DataAsync(await client.GetAsync($"/api/v1/Leads/{leadId}/emails/{messageId}"))).GetProperty("subject").GetString().Should().Be("Hola Ana — Renovación");
    }

    [Fact]
    public async Task FromThePerson_TheLeadIsOptional_AndMustBeOpen()
    {
        var (companyId, client, _) = await CreateCompanyWithUserAsync(factory);
        var personId = await NewPersonAsync(client, companyId, ($"{Guid.NewGuid():N}@cliente.test", PersonContactType.PrimaryEmail, true));
        var leadId = await NewLeadAsync(client, personId);

        var alone = await DataAsync(await client.PostAsJsonAsync($"/api/v1/Persons/{personId}/emails", Mail()));
        alone.TryGetProperty("leadInteractionId", out var none).Should().BeTrue();
        none.ValueKind.Should().Be(JsonValueKind.Null);
        var withLead = await DataAsync(await client.PostAsJsonAsync($"/api/v1/Persons/{personId}/emails", Mail(leadId: leadId)));
        withLead.GetProperty("leadInteractionId").GetGuid().Should().NotBeEmpty();
        (await DataAsync(await client.GetAsync($"/api/v1/Persons/{personId}/emails"))).GetProperty("totalCount").GetInt32().Should().Be(2);
        (await DataAsync(await client.GetAsync($"/api/v1/Leads/{leadId}/emails"))).GetProperty("totalCount").GetInt32().Should().Be(1);

        foreach (var status in new[] { LeadStatus.Won, LeadStatus.Lost, LeadStatus.Inactive })
        {
            await SetLeadStatusAsync(leadId, status);
            var before = await CountMessagesAsync(personId);
            await ExpectAsync(client.PostAsJsonAsync($"/api/v1/Persons/{personId}/emails", Mail(leadId: leadId)), HttpStatusCode.Conflict, "LEAD_NOT_OPEN");
            (await CountMessagesAsync(personId)).Should().Be(before, "nothing is sent nor saved");
        }
    }

    [Fact]
    public async Task TheProviderFails_SavesFailed_WithoutContact_AndAnswers502()
    {
        var (companyId, client, _) = await CreateCompanyWithUserAsync(factory);
        var personId = await NewPersonAsync(client, companyId, ($"{Guid.NewGuid():N}{CapturingEmailService.RejectedDomain}", PersonContactType.PrimaryEmail, true));
        var leadId = await NewLeadAsync(client, personId);

        var response = await client.PostAsJsonAsync($"/api/v1/Leads/{leadId}/emails", Mail());
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway, body);
        body.Should().Contain("EMAIL_SEND_FAILED");
        var messageId = Data(body).GetProperty("id").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.PersonEmailMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == messageId)).Status.Should().Be(PersonEmailStatus.Failed);
        (await db.LeadInteractions.IgnoreQueryFilters().CountAsync(i => i.LeadId == leadId)).Should().Be(0);
    }

    [Fact]
    public async Task TheSenderOfTheCompany_AndTheCustomerEndpoint()
    {
        var (companyId, client, _) = await CreateCompanyWithUserAsync(factory);
        var settings = await DataAsync(await client.GetAsync("/api/v1/EmailCompanySettings"));
        settings.GetProperty("maxEmailsPerUserPerDay").GetInt32().Should().Be(50);
        settings.GetProperty("maxAttachmentsTotalBytes").GetInt64().Should().Be(15L * 1024 * 1024);
        await DataAsync(await client.PutAsJsonAsync("/api/v1/EmailCompanySettings",
            new { fromEmail = "ventas@acme.test", fromName = "Ventas", maxEmailsPerUserPerDay = 50, maxAttachmentsTotalBytes = 15L * 1024 * 1024 }));
        (await client.PostAsJsonAsync("/api/v1/EmailCompanySettings",
            new { maxEmailsPerUserPerDay = 50, maxAttachmentsTotalBytes = 1024 })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var email = $"{Guid.NewGuid():N}@cliente.test";
        var personId = await NewPersonAsync(client, companyId, (email, PersonContactType.PrimaryEmail, true));
        var customerId = await NewCustomerAsync(companyId, personId);

        (await client.PostAsJsonAsync($"/api/v1/Customers/{customerId}/emails", Mail())).StatusCode.Should().Be(HttpStatusCode.Created);
        var captured = Capture.LastMessageTo(email)!;
        captured.FromEmail.Should().Be("ventas@acme.test");
        captured.FromName.Should().Be("Ventas");
        var history = await DataAsync(await client.GetAsync($"/api/v1/Customers/{customerId}/emails"));
        history.GetProperty("items")[0].GetProperty("origin").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Attachments_FromAProductAndFromTheLead_WithinTheLimits()
    {
        var (companyId, client, _) = await CreateCompanyWithUserAsync(factory);
        var email = $"{Guid.NewGuid():N}@cliente.test";
        var personId = await NewPersonAsync(client, companyId, (email, PersonContactType.PrimaryEmail, true));
        var leadId = await NewLeadAsync(client, personId);
        var otherLeadId = await NewLeadAsync(client, personId);
        var productDocumentId = await NewProductDocumentAsync(client);
        var leadDocumentId = await NewLeadDocumentAsync(client, leadId);
        var otherLeadDocumentId = await NewLeadDocumentAsync(client, otherLeadId);

        // SPEC 72.1: the candidates travel wrapped with the limit of attachments of the company.
        var payload = await DataAsync(await client.GetAsync($"/api/v1/Persons/{personId}/emails/attachment-candidates?leadId={leadId}&search=producto"));
        var candidates = payload.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("sourceId").GetGuid()).ToList();
        candidates.Should().Contain([productDocumentId, leadDocumentId]).And.NotContain(otherLeadDocumentId);
        payload.GetProperty("maxAttachmentsTotalBytes").GetInt64().Should().Be(15L * 1024 * 1024);

        var response = await client.PostAsJsonAsync($"/api/v1/Leads/{leadId}/emails",
            Mail(attachments: [new { sourceKind = 1, sourceId = productDocumentId }, new { sourceKind = 2, sourceId = leadDocumentId }]));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        Capture.LastMessageTo(email)!.Attachments.Select(a => a.FileName).Should().BeEquivalentTo(["ficha.pdf", "propuesta.pdf"]);

        await ExpectAsync(client.PostAsJsonAsync($"/api/v1/Leads/{leadId}/emails",
            Mail(attachments: [new { sourceKind = 2, sourceId = otherLeadDocumentId }])), HttpStatusCode.NotFound, "EMAIL_ATTACHMENT_NOT_FOUND");

        await DataAsync(await client.PutAsJsonAsync("/api/v1/EmailCompanySettings", new { maxEmailsPerUserPerDay = 50, maxAttachmentsTotalBytes = 4 }));
        await ExpectAsync(client.PostAsJsonAsync($"/api/v1/Leads/{leadId}/emails",
            Mail(attachments: [new { sourceKind = 1, sourceId = productDocumentId }])), HttpStatusCode.BadRequest, "EMAIL_ATTACHMENTS_TOO_LARGE");
    }

    [Fact]
    public async Task TheDailyQuota_CountsSentAndFailed_PerUser()
    {
        var (companyId, client, _) = await CreateCompanyWithUserAsync(factory);
        await DataAsync(await client.PutAsJsonAsync("/api/v1/EmailCompanySettings", new { maxEmailsPerUserPerDay = 2, maxAttachmentsTotalBytes = 1024 }));
        var good = await NewPersonAsync(client, companyId, ($"{Guid.NewGuid():N}@cliente.test", PersonContactType.PrimaryEmail, true));
        var bad = await NewPersonAsync(client, companyId, ($"{Guid.NewGuid():N}{CapturingEmailService.RejectedDomain}", PersonContactType.PrimaryEmail, true));

        (await client.PostAsJsonAsync($"/api/v1/Persons/{good}/emails", Mail())).StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsJsonAsync($"/api/v1/Persons/{bad}/emails", Mail())).StatusCode.Should().Be(HttpStatusCode.BadGateway);
        await ExpectAsync(client.PostAsJsonAsync($"/api/v1/Persons/{good}/emails", Mail()), (HttpStatusCode)429, "EMAIL_DAILY_LIMIT_REACHED");
    }

    [Fact]
    public async Task SendingFromThePerson_NeedsCanExecute()
    {
        var (companyId, _, _) = await CreateCompanyWithUserAsync(factory);
        var (reader, _) = await AddUserAsync(factory, companyId, "Supervisor");
        await GrantAsync(factory, companyId, "Supervisor", "Persons");
        var personId = await NewPersonAsync(reader, companyId, ($"{Guid.NewGuid():N}@cliente.test", PersonContactType.PrimaryEmail, true));

        (await reader.GetAsync($"/api/v1/Persons/{personId}/email-addresses")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await reader.PostAsJsonAsync($"/api/v1/Persons/{personId}/emails", Mail())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NewSql_UsesTheIndexes()
    {
        var (companyId, _, _) = await CreateCompanyWithUserAsync(factory);
        string[] queries =
        [
            "SELECT m.Id FROM admin.personemailmessages m LEFT JOIN customer.leads l ON l.Id = m.LeadId WHERE m.CompanyId = @c AND m.GcRecord = 0 AND m.PersonId = @p ORDER BY m.SentAt DESC LIMIT 20 OFFSET 0",
            "SELECT COUNT(*) FROM admin.personemailmessages m WHERE m.CompanyId = @c AND m.GcRecord = 0 AND m.LeadId = @p",
            "SELECT m.Id FROM admin.personemailmessages m WHERE m.Id = @p AND m.CompanyId = @c AND m.GcRecord = 0 AND m.PersonId = @p",
            "SELECT a.Id FROM admin.personemailmessageattachments a WHERE a.PersonEmailMessageId = @p AND a.CompanyId = @c AND a.GcRecord = 0",
            "SELECT COUNT(*) FROM admin.personemailmessages m WHERE m.CompanyId = @c AND m.SentByUserId = @p AND m.SentAt >= now() AND m.GcRecord = 0",
            "SELECT pc.Id FROM admin.personcontacts pc WHERE pc.PersonId = @p AND pc.CompanyId = @c AND pc.GcRecord = 0 AND pc.IsActive = TRUE AND pc.ContactType IN (1, 2)",
            "SELECT d.Id FROM customer.leaddocuments d INNER JOIN customer.leads l ON l.Id = d.LeadId WHERE d.LeadId = @p AND d.CompanyId = @c AND d.GcRecord = 0",
            "SELECT Id FROM admin.emailcompanysettings WHERE CompanyId = @c AND GcRecord = 0"
        ];

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using (var off = new NpgsqlCommand("SET enable_seqscan = off", connection))
        {
            await off.ExecuteNonQueryAsync();
        }

        foreach (var sql in queries)
        {
            await using var command = new NpgsqlCommand("EXPLAIN " + sql.Replace("@p", $"'{Guid.NewGuid()}'").Replace("@c", $"'{companyId}'"), connection);
            var plan = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                plan.Add(reader.GetString(0));
            }

            string.Join(Environment.NewLine, plan).Should().NotContain("Seq Scan on personemailmessages", sql)
                .And.NotContain("Seq Scan on personemailmessageattachments", sql);
        }
    }

    // ---- helpers ----

    private static object Mail(string subject = "Hola {{Persona.Nombre}}", string? to = null, Guid? leadId = null, object[]? attachments = null) => new
    {
        toAddress = to,
        cc = Array.Empty<string>(),
        subject,
        bodyHtml = "<p>Le escribe {{Usuario.Nombre}}</p>",
        leadId,
        attachments = attachments ?? []
    };

    private static async Task ExpectAsync(Task<HttpResponseMessage> call, HttpStatusCode status, string code)
    {
        var response = await call;
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);
        body.Should().Contain(code);
    }

    /// <summary>A person created through the API (legal person, so no gender is needed) with the given e-mail contacts.</summary>
    private async Task<Guid> NewPersonAsync(HttpClient client, Guid companyId, params (string Value, PersonContactType Type, bool Primary)[] contacts)
    {
        var response = await client.PostAsJsonAsync("/api/v1/Persons", new { personType = 2, firstName = "Ana", lastName = "López" });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        using var document = JsonDocument.Parse(body);
        var personId = document.RootElement.GetProperty("data").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        foreach (var (value, type, primary) in contacts)
        {
            var contact = PersonContact.Create(companyId, personId, type, value);
            if (primary)
            {
                contact.SetAsPrimary();
            }

            db.PersonContacts.Add(contact);
        }

        await db.SaveChangesAsync();
        return personId;
    }

    private static async Task<Guid> NewLeadAsync(HttpClient client, Guid personId)
    {
        var catalogs = await DataAsync(await client.GetAsync("/api/v1/Leads/catalogs"));
        var relationshipTypeId = catalogs.GetProperty("relationshipTypes").EnumerateArray()
            .Single(r => r.GetProperty("code").GetString() == "PROSPECT_CUSTOMER").GetProperty("id").GetGuid();
        var leadSourceId = catalogs.GetProperty("sources").EnumerateArray()
            .Single(s => s.GetProperty("code").GetString() == "WEB").GetProperty("id").GetGuid();
        var response = await client.PostAsJsonAsync("/api/v1/Leads", new { personId, topic = "Renovación", relationshipTypeId, leadSourceId });
        return (await DataAsync(response)).GetProperty("lead").GetProperty("id").GetGuid();
    }

    private async Task<Guid> NewCustomerAsync(Guid companyId, Guid personId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stageId = await db.CustomerStages.IgnoreQueryFilters().Where(s => s.CompanyId == companyId && s.GcRecord == 0).Select(s => s.Id).FirstAsync();
        var userId = await db.Users.Select(u => u.Id).FirstAsync();
        var customer = JOIN.Domain.Customers.Customer.Create(companyId, personId, userId, $"C-{Random.Shared.Next(1000, 9999)}", stageId);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private static async Task<Guid> NewProductDocumentAsync(HttpClient client)
    {
        var productId = (await DataAsync(await client.PostAsJsonAsync("/api/v1/Products",
            new { code = $"SKU-{Guid.NewGuid():N}"[..16], name = "Producto con ficha", kind = 1, referencePrice = 1m }))).GetProperty("id").GetGuid();
        var typeId = (await DataAsync(await client.GetAsync("/api/v1/ProductDocumentTypes"))).EnumerateArray()
            .Single(t => t.GetProperty("code").GetString() == "TECH_SHEET").GetProperty("id").GetGuid();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([0x25, 0x50, 0x44, 0x46, 0x0A]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "ficha.pdf");
        form.Add(new StringContent(typeId.ToString()), "productDocumentTypeId");
        return (await DataAsync(await client.PostAsync($"/api/v1/Products/{productId}/documents", form))).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> NewLeadDocumentAsync(HttpClient client, Guid leadId)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([0x25, 0x50, 0x44, 0x46, 0x0A]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "propuesta.pdf");
        return (await DataAsync(await client.PostAsync($"/api/v1/leads/{leadId}/documents", form))).GetProperty("id").GetGuid();
    }

    private async Task SetLeadStatusAsync(Guid leadId, LeadStatus status)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Leads.IgnoreQueryFilters().Where(l => l.Id == leadId).ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, status));
    }

    private async Task<int> CountMessagesAsync(Guid personId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.PersonEmailMessages.IgnoreQueryFilters().CountAsync(m => m.PersonId == personId);
    }

    private async Task<string> UserEmailAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Users.Where(u => u.Id == userId).Select(u => u.Email!).SingleAsync();
    }
}
