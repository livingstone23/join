// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JOIN.Domain.Admin;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.IntegrationTests.Security;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static JOIN.IntegrationTests.Customers.CommercialTestHelpers;

namespace JOIN.IntegrationTests.Customers;

/// <summary>
/// SPEC 65 — next action as a calendar event and converting a pending person into a lead, against PostgreSQL 17
/// (criteria 1, 3, 4 and 5). JOIN-001 is used because it has the calendar configured by the development seed.
/// </summary>
public sealed class LeadsCalendarPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public LeadsCalendarPostgreSqlTests(CustomWebApplicationFactory factory) => _factory = factory;

    internal static Guid Find(JsonElement catalogs, string list, string code) =>
        catalogs.GetProperty(list).EnumerateArray().Single(i => i.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();

    /// <summary>
    /// Points the JOIN-001 ONLINE_MEETING contact type at the REMINDER activity (any time, overlap allowed) so the test
    /// does not depend on business hours.
    /// </summary>
    internal static async Task UseReminderActivityAsync(CustomWebApplicationFactory factory, Guid contactTypeCompanyId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var copy = await db.ContactTypeCompanies.IgnoreQueryFilters().SingleAsync(c => c.Id == contactTypeCompanyId);
        copy.CalendarActivityCompanyId = await (from ac in db.CalendarActivityCompanies.IgnoreQueryFilters()
                                                join a in db.CalendarActivities.IgnoreQueryFilters() on ac.CalendarActivityId equals a.Id
                                                where ac.CompanyId == copy.CompanyId && a.Code == CalendarCodes.Activities.Reminder && ac.GcRecord == 0
                                                select ac.Id).FirstAsync();
        await db.SaveChangesAsync();
    }

    internal static async Task<Guid> LeadAsync(HttpClient client, JsonElement catalogs)
    {
        var response = await client.PostAsJsonAsync("/api/v1/Leads", new
        {
            newPerson = new { personType = 2, firstName = "Agenda", lastName = "SRL", commercialName = $"Agenda {Guid.NewGuid():N}"[..20] },
            topic = "Demo del sistema", relationshipTypeId = Find(catalogs, "relationshipTypes", "PROSPECT_CUSTOMER"), leadSourceId = Find(catalogs, "sources", "WEB")
        });
        return (await DataAsync(response)).GetProperty("lead").GetProperty("id").GetGuid();
    }

    internal static object Contact(JsonElement catalogs, string type, object? nextAction) => new
    {
        contactTypeCompanyId = Find(catalogs, "contactTypes", "EMAIL"), summary = $"Contacto por {type}", outcome = 1, nextAction
    };

    [Fact]
    public async Task NextAction_CreatesTheEventInTheOwnersCalendar_LinkedToTheLead()
    {
        using var client = await ModuleTestHelpers.LoginAsync(_factory, "lcano@join.com");
        var catalogs = await DataAsync(await client.GetAsync("/api/v1/Leads/catalogs"));
        var meeting = Find(catalogs, "contactTypes", "ONLINE_MEETING");
        catalogs.GetProperty("contactTypes").EnumerateArray().Single(c => c.GetProperty("code").GetString() == "ONLINE_MEETING")
            .GetProperty("calendarActivityCompanyId").ValueKind.Should().NotBe(JsonValueKind.Null, "the seed links schedulable types to a calendar activity");
        await UseReminderActivityAsync(_factory, meeting);
        var leadId = await LeadAsync(client, catalogs);
        var start = DateTime.UtcNow.Date.AddDays(3).AddHours(15);

        var response = await client.PostAsJsonAsync($"/api/v1/Leads/{leadId}/interactions",
            Contact(catalogs, "correo", new { contactTypeCompanyId = meeting, startUtc = start, endUtc = start.AddMinutes(30), title = "Demo agendada", videoCallUrl = "https://meet.example/demo" }));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var eventId = (await DataAsync(response)).GetProperty("nextActionEventId").GetGuid();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var calendarEvent = await db.CalendarEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == eventId);
            var lead = await db.Leads.IgnoreQueryFilters().SingleAsync(l => l.Id == leadId);
            calendarEvent.LeadId.Should().Be(leadId);
            calendarEvent.OwnerUserId.Should().Be(lead.OwnerUserId);
            calendarEvent.OriginType.Should().Be(CalendarEventOriginType.User);
            calendarEvent.Title.Should().Be("Demo agendada");
        }

        var events = (await DataAsync(await client.GetAsync($"/api/v1/Leads/{leadId}/events"))).EnumerateArray().ToList();
        events.Should().ContainSingle(e => e.GetProperty("id").GetGuid() == eventId);
        events.Single().GetProperty("statusCode").GetString().Should().NotBeNullOrEmpty();
        (await DataAsync(await client.GetAsync($"/api/v1/Leads/{leadId}/timeline"))).EnumerateArray()
            .Should().Contain(t => t.GetProperty("kind").GetString() == "Event" && t.GetProperty("id").GetGuid() == eventId);
    }

    [Fact]
    public async Task NextAction_WithATypeWithoutActivity_Is400()
    {
        using var client = await ModuleTestHelpers.LoginAsync(_factory, "lcano@join.com");
        var catalogs = await DataAsync(await client.GetAsync("/api/v1/Leads/catalogs"));
        var leadId = await LeadAsync(client, catalogs);
        var start = DateTime.UtcNow.Date.AddDays(3).AddHours(16);

        var response = await client.PostAsJsonAsync($"/api/v1/Leads/{leadId}/interactions",
            Contact(catalogs, "correo", new { contactTypeCompanyId = Find(catalogs, "contactTypes", "EMAIL"), startUtc = start, endUtc = start.AddMinutes(30) }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("CONTACT_TYPE_NOT_SCHEDULABLE");
    }

    [Fact]
    public async Task ConvertPending_CreatesAnOpenLeadWithOriginPending_AndASecondConversionIs409()
    {
        var (companyId, client, _) = await CreateCompanyWithUserAsync(_factory);
        var catalogs = await DataAsync(await client.GetAsync("/api/v1/Leads/catalogs"));
        Guid pendingId;
        Guid personId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var channel = await db.CommunicationChannels.IgnoreQueryFilters().Where(c => c.Code == CommunicationChannelCodes.WhatsApp && c.GcRecord == 0).Select(c => c.Id).FirstAsync();
            var person = new Person { CompanyId = companyId, PersonType = PersonType.Physical, FirstName = "Pendiente", LastName = "WhatsApp" };
            var pending = new PersonPendingConfirmation
            {
                CompanyId = companyId, PersonId = person.Id, Reason = "Escribió por WhatsApp", CommunicationChannelId = channel, ChannelIdentifier = "+18095550199", RequestedUtc = DateTime.UtcNow
            };
            db.Persons.Add(person);
            db.PersonPendingConfirmations.Add(pending);
            await db.SaveChangesAsync();
            (pendingId, personId) = (pending.Id, person.Id);
        }

        var body = new { topic = "Pidió precios", relationshipTypeId = Find(catalogs, "relationshipTypes", "PROSPECT_CUSTOMER"), notes = "Atendido" };
        var converted = await client.PostAsJsonAsync($"/api/v1/PersonPendingConfirmations/{pendingId}/convert-to-lead", body);
        converted.StatusCode.Should().Be(HttpStatusCode.OK, await converted.Content.ReadAsStringAsync());
        var leadId = (await DataAsync(converted)).GetProperty("leadId").GetGuid();

        var lead = await DataAsync(await client.GetAsync($"/api/v1/Leads/{leadId}"));
        lead.GetProperty("status").GetInt32().Should().Be(1);
        lead.GetProperty("leadSourceId").GetGuid().Should().Be(Find(catalogs, "sources", "PENDING"));
        lead.GetProperty("personId").GetGuid().Should().Be(personId);

        var queue = (await DataAsync(await client.GetAsync("/api/v1/PersonPendingConfirmations?status=5"))).GetProperty("items").EnumerateArray().ToList();
        queue.Should().ContainSingle(p => p.GetProperty("id").GetGuid() == pendingId && p.GetProperty("leadId").GetGuid() == leadId);

        await using (var check = _factory.Services.CreateAsyncScope())
        {
            var db = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Customers.IgnoreQueryFilters().AnyAsync(c => c.PersonId == personId)).Should().BeFalse("linking never activates customers");
            (await db.PersonPendingConfirmations.IgnoreQueryFilters().SingleAsync(p => p.Id == pendingId)).Status.Should().Be(PersonPendingConfirmationStatus.ConvertedToLead);
        }

        var again = await client.PostAsJsonAsync($"/api/v1/PersonPendingConfirmations/{pendingId}/convert-to-lead", body);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.Content.ReadAsStringAsync()).Should().Contain("PERSON_PENDING_NOT_PENDING");
    }
}

/// <summary>
/// SPEC 65 criterion 2 with <c>Modules:EnforceCompanyModules = true</c>: without the Calendar module, scheduling answers
/// 403 <c>MODULE_NOT_ENABLED</c> and a contact without next action is still registered.
/// </summary>
public sealed class LeadsCalendarModulePostgreSqlTests : IClassFixture<ModuleEnforcementPostgreSqlWebApplicationFactory>
{
    private readonly ModuleEnforcementPostgreSqlWebApplicationFactory _factory;

    public LeadsCalendarModulePostgreSqlTests(ModuleEnforcementPostgreSqlWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task WithoutTheCalendarModule_SchedulingIs403_ButTheContactIsRegistered()
    {
        using var superAdmin = await ModuleTestHelpers.LoginAsync(_factory, "lcano@join.com");
        var calendarAssignment = await ModuleTestHelpers.JoinCompanyModuleIdAsync(_factory, "Calendar");
        using var manager = await ModuleTestHelpers.LoginAsync(_factory, "manager@join.com");
        var catalogs = await DataAsync(await manager.GetAsync("/api/v1/Leads/catalogs"));
        var meeting = LeadsCalendarPostgreSqlTests.Find(catalogs, "contactTypes", "ONLINE_MEETING");
        await LeadsCalendarPostgreSqlTests.UseReminderActivityAsync(_factory, meeting);
        var leadId = await LeadsCalendarPostgreSqlTests.LeadAsync(manager, catalogs);
        var start = DateTime.UtcNow.Date.AddDays(4).AddHours(15);

        await ModuleTestHelpers.SetCompanyModuleActiveAsync(superAdmin, calendarAssignment, isActive: false);
        try
        {
            var blocked = await manager.PostAsJsonAsync($"/api/v1/Leads/{leadId}/interactions",
                LeadsCalendarPostgreSqlTests.Contact(catalogs, "correo", new { contactTypeCompanyId = meeting, startUtc = start, endUtc = start.AddMinutes(30) }));
            blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var problem = await blocked.Content.ReadAsStringAsync();
            problem.Should().Contain("MODULE_NOT_ENABLED").And.Contain("Calendar");

            var plain = await manager.PostAsJsonAsync($"/api/v1/Leads/{leadId}/interactions", LeadsCalendarPostgreSqlTests.Contact(catalogs, "correo", null));
            plain.StatusCode.Should().Be(HttpStatusCode.Created, await plain.Content.ReadAsStringAsync());
        }
        finally
        {
            await ModuleTestHelpers.SetCompanyModuleActiveAsync(superAdmin, calendarAssignment, isActive: true);
        }
    }
}
