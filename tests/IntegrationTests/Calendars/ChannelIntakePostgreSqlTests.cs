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
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Enums;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace JOIN.IntegrationTests.Calendars;

/// <summary>
/// SPEC 45-post (PostgreSQL, <see cref="PostgreSqlWebApplicationFactory"/>) — the channel agent through the API with
/// the seeded <c>agent@join.com</c> in <c>JOIN-001</c>: companies, identification types, person lookup (digits, last
/// 8 digits, e-mail without case), registration with the review activity, booking on a free slot, deduplication
/// (sequential and concurrent), reschedule and cancel; the pending-persons inbox with the seeded supervisor; and the
/// permission boundaries between both resources. Dates in April 2027 (America/Managua, UTC-6).
/// </summary>
public sealed class ChannelIntakePostgreSqlTests : IClassFixture<PostgreSqlWebApplicationFactory>
{
    private const string Base = "/api/v1/CalendarChannelIntake";
    private const string SeedPassword = "ABCabc123*";

    private readonly PostgreSqlWebApplicationFactory _factory;

    public ChannelIntakePostgreSqlTests(PostgreSqlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seed_ShouldCreateTheAgentTheOptionsTheTableAndItsIndexes()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var intake = await db.SystemOptions.IgnoreQueryFilters().SingleAsync(o => o.Name == "CalendarChannelIntake");
        intake.Route.Should().Be("/calendar/channel-intake");
        intake.IsVisibleMenu.Should().BeFalse();
        intake.ControllerName.Should().Be("CalendarChannelIntake");
        var pending = await db.SystemOptions.IgnoreQueryFilters().SingleAsync(o => o.Name == "PendingPersons");
        pending.Route.Should().Be("/customers/pending-persons");
        pending.ControllerName.Should().Be("PersonPendingConfirmations");

        var agent = await db.ApplicationUsers.IgnoreQueryFilters().SingleAsync(u => u.Email == "agent@join.com");
        agent.IsActive.Should().BeTrue();

        await using var connection = new NpgsqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT indexname || ' ' || indexdef FROM pg_indexes WHERE schemaname = 'admin' AND tablename = 'personpendingconfirmations'", connection);
        var indexes = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                indexes.Add(reader.GetString(0));
            }
        }

        indexes.Should().Contain(i => i.StartsWith("ux_personpendingconfirmations_company_person_pending") && i.Contains("UNIQUE") && i.Contains("WHERE ((status = 1) AND (gcrecord = 0))"));
        indexes.Should().Contain(i => i.StartsWith("ix_personpendingconfirmations_company_status_requestedutc") && i.Contains("WHERE (gcrecord = 0)"));
    }

    [Fact]
    public async Task Agent_RegistersBooksReschedulesAndCancels()
    {
        using var agent = await LoginAsync("agent@join.com");
        var phone = NewPhone();
        var identification = $"ID-{Guid.NewGuid():N}"[..20];

        var companies = await DataAsync(await agent.GetAsync($"{Base}/companies?search=join-001"));
        companies.EnumerateArray().Should().Contain(c => c.GetProperty("taxId").GetString() == "JOIN-001");
        var types = await DataAsync(await agent.GetAsync($"{Base}/identification-types"));
        var typeId = types[0].GetProperty("id").GetGuid();

        // Unknown number → register.
        (await DataAsync(await agent.GetAsync($"{Base}/persons/lookup?channelCode=WHATSAPP&identifier={Uri.EscapeDataString(phone)}")))
            .GetProperty("found").GetBoolean().Should().BeFalse();
        var registered = await agent.PostAsJsonAsync($"{Base}/persons", PersonBody(typeId, identification, phone));
        var registeredData = Data(await registered.Content.ReadAsStringAsync());
        registered.StatusCode.Should().Be(HttpStatusCode.Created, registeredData.ToString());
        var personId = registeredData.GetProperty("personId").GetGuid();
        var reviewId = registeredData.GetProperty("reviewEventId").GetGuid();

        // Same identification → 200, nothing created; the same phone for another identification → 409.
        var again = await agent.PostAsJsonAsync($"{Base}/persons", PersonBody(typeId, identification, NewPhone()));
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(await again.Content.ReadAsStringAsync()).GetProperty("created").GetBoolean().Should().BeFalse();
        await ShouldFailAsync(await agent.PostAsJsonAsync($"{Base}/persons", PersonBody(typeId, identification + "X", phone)),
            HttpStatusCode.Conflict, "CALENDAR_INTAKE_CONTACT_BELONGS_TO_OTHER_PERSON");

        // Lookup by another format of the same number, and by identification: id and name only.
        var formatted = $"+505 {phone[4..8]}-{phone[8..]}";
        var byPhone = await DataAsync(await agent.GetAsync($"{Base}/persons/lookup?channelCode=whatsapp&identifier={Uri.EscapeDataString(formatted)}"));
        byPhone.GetProperty("personId").GetGuid().Should().Be(personId);
        byPhone.GetProperty("hasPendingConfirmation").GetBoolean().Should().BeTrue();
        byPhone.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["found", "personId", "fullName", "hasPendingConfirmation", "ambiguous"]);
        var byLocal = await DataAsync(await agent.GetAsync($"{Base}/persons/lookup?channelCode=WHATSAPP&identifier={Uri.EscapeDataString("+1" + phone[^8..])}"));
        byLocal.GetProperty("personId").GetGuid().Should().Be(personId, "a number is also matched by its last 8 digits");
        var byId = await DataAsync(await agent.GetAsync($"{Base}/persons/lookup?identificationTypeId={typeId}&identificationNumber={identification}"));
        byId.GetProperty("personId").GetGuid().Should().Be(personId);

        // The person's activities never include the internal review.
        (await DataAsync(await agent.GetAsync($"{Base}/events?requesterPersonId={personId}&from=2026-01-01T00:00:00Z&to=2026-03-31T00:00:00Z")))
            .GetArrayLength().Should().Be(0);
        await ShouldFailAsync(await agent.PostAsJsonAsync($"{Base}/events/{reviewId}/cancel", CancelBody(personId)), HttpStatusCode.NotFound, "CALENDAR_EVENT_NOT_FOUND");

        // Default user by exact e-mail (any case); unknown e-mail → not found.
        var defaultUser = await DefaultUserAsync();
        var user = await DataAsync(await agent.GetAsync($"{Base}/users/lookup?email={Uri.EscapeDataString(defaultUser.Email.ToUpperInvariant())}"));
        user.GetProperty("userId").GetGuid().Should().Be(defaultUser.Id);
        (await DataAsync(await agent.GetAsync($"{Base}/users/lookup?email=nadie@join.com"))).GetProperty("found").GetBoolean().Should().BeFalse();

        // Book on the first free slot of the default user.
        var slot = await FirstSlotAsync(agent, "2027-04-05T06:00:00Z", "2027-04-06T06:00:00Z");
        var reference = $"wamid.{Guid.NewGuid():N}";
        var created = await agent.PostAsJsonAsync($"{Base}/events", EventBody(personId, phone, slot, reference, targetEmail: "nadie@join.com"));
        var createdData = Data(await created.Content.ReadAsStringAsync());
        created.StatusCode.Should().Be(HttpStatusCode.Created, createdData.ToString());
        createdData.GetProperty("routedToDefaultUser").GetBoolean().Should().BeTrue();
        createdData.GetProperty("status").GetProperty("code").GetString().Should().NotBeNullOrEmpty();
        var eventId = createdData.GetProperty("eventId").GetGuid();

        var duplicate = await agent.PostAsJsonAsync($"{Base}/events", EventBody(personId, phone, slot, reference));
        duplicate.StatusCode.Should().Be(HttpStatusCode.OK);
        var duplicateData = Data(await duplicate.Content.ReadAsStringAsync());
        duplicateData.GetProperty("isDuplicate").GetBoolean().Should().BeTrue();
        duplicateData.GetProperty("eventId").GetGuid().Should().Be(eventId);

        var listed = await DataAsync(await agent.GetAsync($"{Base}/events?requesterPersonId={personId}&from=2027-04-01T00:00:00Z&to=2027-04-30T00:00:00Z"));
        listed.EnumerateArray().Select(e => e.GetProperty("eventId").GetGuid()).Should().Equal(eventId);

        // Reschedule to the next free slot (idempotent by message id), then cancel.
        var next = await FirstSlotAsync(agent, "2027-04-07T06:00:00Z", "2027-04-08T06:00:00Z");
        var rescheduleReference = $"wamid.{Guid.NewGuid():N}";
        var rescheduled = await DataAsync(await agent.PutAsJsonAsync($"{Base}/events/{eventId}/reschedule", RescheduleBody(personId, next, rescheduleReference)));
        rescheduled.GetProperty("status").GetProperty("code").GetString().Should().Be("RESCHEDULED");
        rescheduled.GetProperty("startUtc").GetDateTime().Should().Be(next.UtcDateTime);
        (await DataAsync(await agent.PutAsJsonAsync($"{Base}/events/{eventId}/reschedule", RescheduleBody(personId, slot, rescheduleReference))))
            .GetProperty("startUtc").GetDateTime().Should().Be(next.UtcDateTime, "a retry does not apply again");
        await ShouldFailAsync(await agent.PutAsJsonAsync($"{Base}/events/{eventId}/reschedule", RescheduleBody(Guid.NewGuid(), slot, "wamid.other")),
            HttpStatusCode.NotFound, "CALENDAR_EVENT_NOT_FOUND");

        (await DataAsync(await agent.PostAsJsonAsync($"{Base}/events/{eventId}/cancel", CancelBody(personId))))
            .GetProperty("status").GetProperty("code").GetString().Should().Be("CANCELLED");
        await ShouldFailAsync(await agent.PostAsJsonAsync($"{Base}/events/{eventId}/cancel", CancelBody(personId)),
            HttpStatusCode.BadRequest, "CALENDAR_INTAKE_EVENT_ALREADY_CLOSED");

        // Stored origin and log: channel, service user and requester identifier.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var whatsApp = await db.CommunicationChannels.IgnoreQueryFilters().Where(c => c.Code == "WHATSAPP").Select(c => c.Id).SingleAsync();
        var agentId = await db.ApplicationUsers.IgnoreQueryFilters().Where(u => u.Email == "agent@join.com").Select(u => u.Id).SingleAsync();
        var stored = await db.CalendarEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == eventId);
        stored.OriginChannelId.Should().Be(whatsApp);
        stored.OriginType.Should().Be(CalendarEventOriginType.Phone);
        stored.OriginReference.Should().Be(reference);
        stored.OriginUserId.Should().Be(agentId);
        stored.Description.Should().Contain("nadie@join.com");
        var logs = await db.CalendarEventLogs.IgnoreQueryFilters().Where(l => l.CalendarEventId == eventId).OrderBy(l => l.OccurredUtc).ToListAsync();
        logs.Select(l => l.Action).Should().Equal(CalendarEventLogAction.Created, CalendarEventLogAction.Rescheduled, CalendarEventLogAction.StatusChanged);
        logs.Should().OnlyContain(l => l.ChannelId == whatsApp && l.ActorUserId == agentId);
        logs.Select(l => l.ActorIdentifier).Should().Equal(phone, "+50500000000", "+50500000000");
        logs[1].Notes.Should().StartWith($"[ref:{rescheduleReference}]");
        logs[2].Notes.Should().Be("Ya no puede asistir");
    }

    [Fact]
    public async Task Agent_ConcurrentRetries_ShouldCreateASingleActivity()
    {
        using var agent = await LoginAsync("agent@join.com");
        var phone = NewPhone();
        var types = await DataAsync(await agent.GetAsync($"{Base}/identification-types"));
        var registered = Data(await (await agent.PostAsJsonAsync($"{Base}/persons",
            PersonBody(types[0].GetProperty("id").GetGuid(), $"R-{Guid.NewGuid():N}"[..20], phone))).Content.ReadAsStringAsync());
        var personId = registered.GetProperty("personId").GetGuid();
        var slot = await FirstSlotAsync(agent, "2027-04-12T06:00:00Z", "2027-04-13T06:00:00Z");
        var reference = $"wamid.{Guid.NewGuid():N}";

        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => agent.PostAsJsonAsync($"{Base}/events", EventBody(personId, phone, slot, reference))));

        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Created || s == HttpStatusCode.OK, string.Join("\n", bodies));
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        bodies.Select(b => Data(b).GetProperty("eventId").GetGuid()).Distinct().Should().ContainSingle();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.CalendarEvents.IgnoreQueryFilters().CountAsync(e => e.OriginReference == reference)).Should().Be(1);
    }

    [Fact]
    public async Task Supervisor_ReviewsThePendingPersonAndClosesTheReview()
    {
        using var agent = await LoginAsync("agent@join.com");
        var phone = NewPhone();
        var types = await DataAsync(await agent.GetAsync($"{Base}/identification-types"));
        var registered = Data(await (await agent.PostAsJsonAsync($"{Base}/persons",
            PersonBody(types[0].GetProperty("id").GetGuid(), $"S-{Guid.NewGuid():N}"[..20], phone))).Content.ReadAsStringAsync());
        var pendingId = registered.GetProperty("pendingConfirmationId").GetGuid();
        var reviewId = registered.GetProperty("reviewEventId").GetGuid();

        using var supervisor = await LoginAsync("supervisor@join.com");
        var inbox = await DataAsync(await supervisor.GetAsync($"/api/v1/PersonPendingConfirmations?search={Uri.EscapeDataString(phone)}"));
        var row = inbox.GetProperty("items").EnumerateArray().Single();
        row.GetProperty("id").GetGuid().Should().Be(pendingId);
        row.GetProperty("channelName").GetString().Should().NotBeNullOrEmpty();
        row.GetProperty("reviewEventStatus").GetString().Should().NotBeNullOrEmpty();

        var detail = await DataAsync(await supervisor.GetAsync($"/api/v1/PersonPendingConfirmations/{pendingId}"));
        detail.GetProperty("contacts")[0].GetProperty("contactValue").GetString().Should().Be(phone);
        detail.GetProperty("events").EnumerateArray().Should().Contain(e => e.GetProperty("eventId").GetGuid() == reviewId);

        await ShouldFailAsync(await supervisor.PostAsJsonAsync($"/api/v1/PersonPendingConfirmations/{pendingId}/resolve", new { status = "Discarded" }),
            HttpStatusCode.BadRequest, "notes are required");
        (await supervisor.PostAsJsonAsync($"/api/v1/PersonPendingConfirmations/{pendingId}/resolve", new { status = "Discarded", notes = "Spam" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await ShouldFailAsync(await supervisor.PostAsJsonAsync($"/api/v1/PersonPendingConfirmations/{pendingId}/resolve", new { status = "Confirmed" }),
            HttpStatusCode.BadRequest, "PERSON_PENDING_ALREADY_RESOLVED");
        (await DataAsync(await supervisor.GetAsync($"/api/v1/PersonPendingConfirmations?status=4&search={Uri.EscapeDataString(phone)}")))
            .GetProperty("items").GetArrayLength().Should().Be(1);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pending = await db.PersonPendingConfirmations.IgnoreQueryFilters().SingleAsync(p => p.Id == pendingId);
        pending.Status.Should().Be(PersonPendingConfirmationStatus.Discarded);
        (await db.Persons.IgnoreQueryFilters().SingleAsync(p => p.Id == pending.PersonId)).IsActive.Should().BeTrue();
        var review = await db.CalendarEvents.IgnoreQueryFilters().Include(e => e.CalendarStatusCompany).ThenInclude(s => s.CalendarStatus)
            .SingleAsync(e => e.Id == reviewId);
        review.CalendarStatusCompany.CalendarStatus.Code.Should().Be(CalendarCodes.Statuses.Completed);
    }

    [Fact]
    public async Task Permissions_ShouldKeepTheAgentAndTheInboxApart()
    {
        using var agent = await LoginAsync("agent@join.com");
        using var supervisor = await LoginAsync("supervisor@join.com");

        (await agent.GetAsync("/api/v1/PersonPendingConfirmations")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await agent.GetAsync("/api/v1/CalendarPanel/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "the agent has no CalendarOthers");
        (await supervisor.GetAsync($"{Base}/identification-types")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ShouldFailAsync(await supervisor.GetAsync($"{Base}/companies"), HttpStatusCode.Forbidden, "CALENDAR_INTAKE_COMPANY_NOT_ALLOWED");
        (await _factory.CreateClient().GetAsync($"{Base}/companies")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static string NewPhone() => $"+5058{Random.Shared.Next(1000000, 9999999)}";

    private static object PersonBody(Guid typeId, string identification, string phone) => new
    {
        personType = 1,
        firstName = "Ana",
        lastName = "Canal",
        identificationTypeId = typeId,
        identificationNumber = identification,
        phone,
        reason = "Quiere una cita para revisar su contrato",
        originChannelCode = "WHATSAPP",
        originIdentifier = phone,
        originReference = $"wamid.{Guid.NewGuid():N}"
    };

    private static object EventBody(Guid personId, string phone, DateTimeOffset start, string reference, string? targetEmail = null) => new
    {
        activityCode = "MEETING",
        title = "Cita: revisión de contrato",
        summary = "Revisar la cláusula 4",
        isAllDay = false,
        start,
        targetUserEmail = targetEmail,
        requesterPersonId = personId,
        originChannelCode = "WHATSAPP",
        originIdentifier = phone,
        originReference = reference
    };

    private static object RescheduleBody(Guid personId, DateTimeOffset start, string reference) => new
    {
        requesterPersonId = personId,
        start,
        originChannelCode = "WHATSAPP",
        originIdentifier = "+50500000000",
        originReference = reference,
        notes = "Prefiere otro día"
    };

    private static object CancelBody(Guid personId) => new
    {
        requesterPersonId = personId,
        reason = "Ya no puede asistir",
        originChannelCode = "WHATSAPP",
        originIdentifier = "+50500000000"
    };

    private static async Task<DateTimeOffset> FirstSlotAsync(HttpClient agent, string from, string to)
    {
        var availability = await DataAsync(await agent.GetAsync($"{Base}/availability?from={from}&to={to}&activityCode=MEETING"));
        availability.GetProperty("timeZone").GetString().Should().Be("America/Managua");
        var slots = availability.GetProperty("slots").EnumerateArray().ToList();
        slots.Should().NotBeEmpty();
        return new DateTimeOffset(slots[Random.Shared.Next(slots.Count)].GetProperty("startUtc").GetDateTime(), TimeSpan.Zero);
    }

    private async Task<(Guid Id, string Email)> DefaultUserAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var joinId = await db.Companies.IgnoreQueryFilters().Where(c => c.TaxId == "JOIN-001").Select(c => c.Id).SingleAsync();
        var userId = await db.CalendarCompanies.IgnoreQueryFilters().Where(c => c.CompanyId == joinId && c.GcRecord == 0).Select(c => c.DefaultActivityUserId).SingleAsync();
        var email = await db.ApplicationUsers.IgnoreQueryFilters().Where(u => u.Id == userId).Select(u => u.Email!).SingleAsync();
        return (userId, email);
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

    private static async Task<string> ShouldFailAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);
        body.Should().Contain(code);
        return body;
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(body);
        return Data(body);
    }

    private static JsonElement Data(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").Clone();
    }
}
