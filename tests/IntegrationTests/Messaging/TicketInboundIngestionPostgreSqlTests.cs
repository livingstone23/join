// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace JOIN.IntegrationTests.Messaging;

/// <summary>
/// SPEC 47-post (PostgreSQL, <see cref="CustomWebApplicationFactory"/>) — inbound ticket ingestion through the
/// API on the seeded <c>JOIN-001</c>: migration indexes and permission seed, the <c>TicketInboundChannels</c> CRUD
/// with the seeded roles, the anonymous WhatsApp and e-mail webhooks (bare 404s, idempotency also under concurrent
/// retries, attachments) and the immediate invalidation of a rotated token.
/// </summary>
public sealed class TicketInboundIngestionPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Base = "/api/v1/TicketInboundChannels";
    private const string SeedPassword = "ABCabc123*";

    private readonly CustomWebApplicationFactory _factory;

    public TicketInboundIngestionPostgreSqlTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Migration_And_Seed_ShouldCreateTheFilteredIndexesTheOptionAndTheRolePermissions()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var option = await db.SystemOptions.IgnoreQueryFilters().SingleAsync(o => o.Name == "TicketInboundChannels");
        option.Route.Should().Be("/tickets/ticket-inbound-channels");
        option.ControllerName.Should().Be("TicketInboundChannels");

        // SPEC 100: JOIN-NI-001 receives the same permissions in Development; the check is on JOIN-001.
        var joinCompanyId = await JoinCompanyIdAsync();
        var permissions = await db.RoleSystemOptions.IgnoreQueryFilters()
            .Where(p => p.SystemOptionId == option.Id && p.CompanyId == joinCompanyId)
            .Join(db.ApplicationRoles, p => p.RoleId, r => r.Id, (p, r) => new { r.Name, p.CanRead, p.CanCreate, p.CanUpdate, p.CanDelete, p.CanExecute })
            .ToListAsync();
        permissions.Single(p => p.Name == "Manager").Should().BeEquivalentTo(new { Name = "Manager", CanRead = true, CanCreate = true, CanUpdate = false, CanDelete = true, CanExecute = true });
        permissions.Single(p => p.Name == "Supervisor").Should().BeEquivalentTo(new { CanRead = true, CanCreate = false, CanUpdate = false, CanDelete = false }, o => o.ExcludingMissingMembers());
        permissions.Single(p => p.Name == "UsuarioSimple").CanRead.Should().BeFalse();
        permissions.Should().OnlyContain(p => !p.CanUpdate || p.Name == "Admin" || p.Name == "SuperAdminCompany");

        var indexes = await IndexesAsync("ticketinboundchannels");
        indexes.Should().Contain(i => i.StartsWith("ux_ticketinboundchannels_token_active") && i.Contains("UNIQUE") && i.Contains("WHERE (gcrecord = 0)"));
        indexes.Should().Contain(i => i.StartsWith("ux_ticketinboundchannels_company_channel_active") && i.Contains("UNIQUE") && i.Contains("WHERE (gcrecord = 0)"));
        (await IndexesAsync("tickets")).Should().Contain(i =>
            i.StartsWith("ux_tickets_company_channel_externalmessageid_active")
            && i.Contains("UNIQUE")
            && i.Contains("WHERE ((externalmessageid IS NOT NULL) AND (gcrecord = 0))"));
    }

    [Fact]
    public async Task Crud_ShouldIssueTheTokenOnceAndRespectTheSeededRoles()
    {
        using var manager = await LoginAsync("manager@join.com");
        using var supervisor = await LoginAsync("supervisor@join.com");
        using var simpleUser = await LoginAsync("simpleuser@join.com");
        var telegramId = await ChannelIdAsync(CommunicationChannelCodes.Telegram);
        var sendGridId = await ChannelIdAsync(CommunicationChannelCodes.SendGrid);
        await EnsureNoActiveChannelAsync(sendGridId);

        (await manager.GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await supervisor.GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await simpleUser.GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await supervisor.PostAsJsonAsync(Base, new { channelId = sendGridId })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await ShouldFailAsync(await manager.PostAsJsonAsync(Base, new { channelId = telegramId }), HttpStatusCode.BadRequest, "UNSUPPORTED_INBOUND_CHANNEL");

        var created = await manager.PostAsJsonAsync(Base, new { channelId = sendGridId, description = "Correo soporte" });
        var data = Data(await created.Content.ReadAsStringAsync());
        created.StatusCode.Should().Be(HttpStatusCode.Created, data.ToString());
        var token = data.GetProperty("webhookToken").GetString()!;
        token.Should().MatchRegex("^[A-Za-z0-9_-]{43,}$");
        data.GetProperty("webhookUrl").GetString().Should().EndWith($"/api/v1/inbound/email/{token}");

        await ShouldFailAsync(await manager.PostAsJsonAsync(Base, new { channelId = sendGridId }), HttpStatusCode.Conflict, "INBOUND_CHANNEL_ALREADY_ACTIVE");

        var listBody = await (await manager.GetAsync(Base)).Content.ReadAsStringAsync();
        listBody.Should().NotContain(token);
        var listed = Data(listBody).EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == data.GetProperty("id").GetGuid());
        listed.GetProperty("webhookTokenMasked").GetString().Should().Be($"••••••••{token[^4..]}");
        listed.GetProperty("webhookUrl").GetString().Should().Contain("/api/v1/inbound/email/••••••••");
    }

    [Fact]
    public async Task WhatsAppWebhook_ShouldCreateOneTicketPerMessageAndAnswerOnlyTheCode()
    {
        using var manager = await LoginAsync("manager@join.com");
        var token = await CreateChannelAsync(manager, CommunicationChannelCodes.WhatsApp);
        using var anonymous = _factory.CreateClient();
        var messageSid = $"SM{Guid.NewGuid():N}";
        var phone = $"+5058{Random.Shared.Next(1000000, 9999999)}";

        // Unknown token and a WhatsApp token on the e-mail URL: bare 404, no body.
        var unknown = await anonymous.PostAsync("/api/v1/inbound/whatsapp/no-such-token", WhatsAppForm(messageSid, phone));
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await unknown.Content.ReadAsStringAsync()).Should().BeEmpty();
        var wrongChannel = await anonymous.PostAsync($"/api/v1/inbound/email/{token}", EmailForm("a@b.com", "s", "t"));
        wrongChannel.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await wrongChannel.Content.ReadAsStringAsync()).Should().BeEmpty();

        var first = await anonymous.PostAsync($"/api/v1/inbound/whatsapp/{token}", WhatsAppForm(messageSid, phone));
        var firstBody = await first.Content.ReadAsStringAsync();
        first.StatusCode.Should().Be(HttpStatusCode.OK, firstBody);
        using var document = JsonDocument.Parse(firstBody);
        document.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("ticketCode");
        var code = document.RootElement.GetProperty("ticketCode").GetString();

        // Sequential and concurrent retries of the same MessageSid return the same ticket.
        var retries = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            anonymous.PostAsync($"/api/v1/inbound/whatsapp/{token}", WhatsAppForm(messageSid, phone))));
        foreach (var retry in retries)
        {
            retry.StatusCode.Should().Be(HttpStatusCode.OK);
            (await retry.Content.ReadAsStringAsync()).Should().Contain(code!);
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ticket = await db.Tickets.IgnoreQueryFilters().Include(t => t.TicketLogs).SingleAsync(t => t.ExternalMessageId == messageSid);
        ticket.Code.Should().Be(code);
        ticket.CompanyId.Should().Be(await JoinCompanyIdAsync());
        ticket.AssignedToUserId.Should().BeNull();
        ticket.IsVisibleToExternals.Should().BeTrue();
        ticket.PersonId.Should().BeNull();
        ticket.Description.Should().StartWith($"Remitente: {phone} ({phone})");
        ticket.CreatedBy.Should().Be("System");
        ticket.TicketLogs.Should().ContainSingle(l => l.LogType == LogType.Creation)
            .Which.Summary.Should().Contain("WhatsApp").And.Contain(phone);
        var isTicketAdmin = await db.TicketUserCompanies.IgnoreQueryFilters()
            .AnyAsync(u => u.CompanyId == ticket.CompanyId && u.UserId == ticket.CreatedByUserId && u.IsSuperAdminTicket && u.GcRecord == 0);
        isTicketAdmin.Should().BeTrue();

        // The ticket is visible to the authenticated module.
        (await manager.GetAsync($"/api/v1/Tickets/{ticket.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EmailWebhook_ShouldStoreTheAttachmentAndRotationShouldInvalidateTheToken()
    {
        using var manager = await LoginAsync("manager@join.com");
        var sendGridId = await ChannelIdAsync(CommunicationChannelCodes.SendGrid);
        await EnsureNoActiveChannelAsync(sendGridId);
        var token = await CreateChannelAsync(manager, CommunicationChannelCodes.SendGrid);
        using var anonymous = _factory.CreateClient();
        var messageId = $"{Guid.NewGuid():N}@mail.cliente.com";

        var form = EmailForm("\"Ana Pérez\" <ana@cliente.com>", "Factura duplicada", "Me cobraron dos veces", $"Message-ID: <{messageId}>");
        var pdf = new ByteArrayContent("%PDF-1.4 test"u8.ToArray());
        pdf.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(pdf, "attachment1", "factura.pdf");
        var executable = new ByteArrayContent([1, 2, 3]);
        executable.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(executable, "attachment2", "virus.exe");
        form.Add(new StringContent("2"), "attachments");

        var response = await anonymous.PostAsync($"/api/v1/inbound/email/{token}", form);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ticket = await db.Tickets.IgnoreQueryFilters().Include(t => t.TicketLogs).SingleAsync(t => t.ExternalMessageId == messageId);
            ticket.Name.Should().Be("Factura duplicada");
            ticket.Description.Should().StartWith("Remitente: Ana Pérez (ana@cliente.com)");
            var documents = await db.TicketDocuments.IgnoreQueryFilters().Where(d => d.TicketId == ticket.Id).ToListAsync();
            documents.Should().ContainSingle().Which.OriginalName.Should().Be("factura.pdf");
            ticket.TicketLogs.Should().ContainSingle(l => l.LogType == LogType.InternalNote)
                .Which.Summary.Should().Contain("virus.exe");
        }

        // Rotation = delete: the very next request with the old token is a bare 404.
        var channelId = Data(await (await manager.GetAsync(Base)).Content.ReadAsStringAsync()).EnumerateArray()
            .Single(i => i.GetProperty("channelCode").GetString() == CommunicationChannelCodes.SendGrid).GetProperty("id").GetGuid();
        (await manager.DeleteAsync($"{Base}/{channelId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var afterDelete = await anonymous.PostAsync($"/api/v1/inbound/email/{token}", EmailForm("ana@cliente.com", "Otra", "Otro mensaje"));
        afterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static FormUrlEncodedContent WhatsAppForm(string messageSid, string phone) => new(new Dictionary<string, string>
    {
        ["MessageSid"] = messageSid,
        ["From"] = $"whatsapp:{phone}",
        ["To"] = "whatsapp:+14155238886",
        ["Body"] = "Hola, mi pedido no llegó",
        ["NumMedia"] = "0"
    });

    private static MultipartFormDataContent EmailForm(string from, string subject, string text, string? headers = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(from), "from" },
            { new StringContent("soporte@join.com"), "to" },
            { new StringContent(subject), "subject" },
            { new StringContent(text), "text" }
        };
        if (headers is not null)
        {
            form.Add(new StringContent(headers), "headers");
        }

        return form;
    }

    private async Task<string> CreateChannelAsync(HttpClient manager, string code)
    {
        var channelId = await ChannelIdAsync(code);
        await EnsureNoActiveChannelAsync(channelId);
        var created = await manager.PostAsJsonAsync(Base, new { channelId });
        var body = await created.Content.ReadAsStringAsync();
        created.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return Data(body).GetProperty("webhookToken").GetString()!;
    }

    /// <summary> Test classes share the container: drop an active row a previous test left for the channel. </summary>
    private async Task EnsureNoActiveChannelAsync(Guid channelId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var companyId = await JoinCompanyIdAsync();
        var active = await db.Set<JOIN.Domain.Messaging.TicketInboundChannel>().IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.ChannelId == channelId && x.GcRecord == 0)
            .ToListAsync();
        active.ForEach(x => x.MarkAsDeleted());
        await db.SaveChangesAsync();
    }

    private async Task<Guid> ChannelIdAsync(string code)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.CommunicationChannels.IgnoreQueryFilters().Where(c => c.Code == code && c.GcRecord == 0).Select(c => c.Id).SingleAsync();
    }

    private async Task<Guid> JoinCompanyIdAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Companies.IgnoreQueryFilters().Where(c => c.TaxId == "JOIN-001").Select(c => c.Id).SingleAsync();
    }

    private async Task<List<string>> IndexesAsync(string table)
    {
        await using var connection = new NpgsqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT indexname || ' ' || indexdef FROM pg_indexes WHERE schemaname = 'messaging' AND tablename = @table", connection);
        command.Parameters.AddWithValue("table", table);
        var indexes = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexes.Add(reader.GetString(0));
        }

        return indexes;
    }

    private async Task<HttpClient> LoginAsync(string email)
    {
        using var unauthClient = _factory.CreateClient();
        var login = await unauthClient.PostAsJsonAsync("/api/v1/users/login", new LoginCommand { Email = email, Password = SeedPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        var payload = await login.Content.ReadFromJsonAsync<Response<LoginResponse>>();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Data!.Token);
        return client;
    }

    private static async Task ShouldFailAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);
        body.Should().Contain(code);
    }

    private static JsonElement Data(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").Clone();
    }
}
