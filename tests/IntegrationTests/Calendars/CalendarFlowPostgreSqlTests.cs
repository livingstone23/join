// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Application.UseCases.Security.Auth.Register;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Calendars;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JOIN.IntegrationTests.Calendars;

/// <summary>
/// SPEC 44-post (PostgreSQL, <see cref="CustomWebApplicationFactory"/>) — calendar flow through the API:
/// configure the company → create an activity → overlap 409 → confirm → week query, plus recurring series with the
/// three scopes, the panel (business hours 422, reschedule status), availability, non-working periods and the seed.
/// Dates are in March 2027 (America/Managua, UTC-6, no daylight saving time).
/// </summary>
public sealed class CalendarFlowPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Creator = "spec44-tests";
    private const string Password = "Integration!Pass123";

    private readonly CustomWebApplicationFactory _factory;

    public CalendarFlowPostgreSqlTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seed_ShouldCreateCalendarModuleCatalogsMenuAndJoinConfiguration()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var module = await db.SystemModules.IgnoreQueryFilters().SingleAsync(m => m.Name == "Calendar");
        module.IsBase.Should().BeFalse();

        (await db.CalendarActivities.IgnoreQueryFilters().Select(a => a.Code).ToListAsync())
            .Should().Contain(["MEETING", "APPOINTMENT", "TASK", "REMINDER", "PERSON_REVIEW"]);
        (await db.CalendarStatuses.IgnoreQueryFilters().Select(s => s.Code).ToListAsync())
            .Should().Contain(["PENDING", "CONFIRMED", "RESCHEDULED", "CANCELLED", "COMPLETED"]);

        var joinId = await db.Companies.IgnoreQueryFilters().Where(c => c.TaxId == "JOIN-001").Select(c => c.Id).SingleAsync();
        var calendarCompany = await db.CalendarCompanies.IgnoreQueryFilters().SingleAsync(c => c.CompanyId == joinId && c.GcRecord == 0);
        calendarCompany.TimeZone.Should().Be("America/Managua");
        (await db.CompanyModules.IgnoreQueryFilters().AnyAsync(cm => cm.CompanyId == joinId && cm.ModuleId == module.Id && cm.IsActive)).Should().BeTrue();
        (await db.CalendarUserConfigurations.IgnoreQueryFilters().CountAsync(c => c.CompanyId == joinId)).Should().BeGreaterThanOrEqualTo(5);

        var routes = await db.SystemOptions.IgnoreQueryFilters().Where(o => o.Route.StartsWith("/calendar")).Select(o => o.Route).ToListAsync();
        routes.Should().Contain(["/calendar", "/calendar/my-calendar", "/calendar/panel", "/calendar/activity-confirmation", "/calendar/settings", "/calendar/catalogs"]);

        // Physical schema: calendar, all lower case: eleven tables of SPEC 44-post plus the three of SPEC 46-post.
        var tables = await db.Database.SqlQueryRaw<string>(
            "SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'calendar'").ToListAsync();
        tables.Should().BeEquivalentTo(["activities", "activitycompanies", "statuses", "statuscompanies", "calendarcompanies",
            "companynonworkingdays", "userconfigurations", "usernonworkingperiods", "usercalendars", "events", "eventlogs",
            "externalconnections", "externaleventlinks", "syncoutbox"]);
    }

    [Fact]
    public async Task Flow_ConfigureCreateOverlapConfirmAndQueryWeek()
    {
        var tenant = await SeedTenantAsync(withModule: true);
        using var client = await CreateClientAsync(tenant, tenant.AdminEmail);

        // Without configuration the operation endpoints answer CALENDAR_COMPANY_NOT_CONFIGURED; current is never 404.
        var current = await ReadDataAsync(await client.GetAsync("/api/v1/CalendarCompanies/current"));
        current.GetProperty("isConfigured").GetBoolean().Should().BeFalse();
        current.GetProperty("moduleEnabled").GetBoolean().Should().BeTrue();
        current.GetProperty("usersWithoutCalendar").GetInt32().Should().Be(2);
        await ShouldFailAsync(await client.GetAsync("/api/v1/CalendarUserConfigurations/me"), HttpStatusCode.BadRequest, "CALENDAR_COMPANY_NOT_CONFIGURED");

        // Invalid zone, then the real configuration (provisions both users).
        await ShouldFailAsync(await client.PostAsJsonAsync("/api/v1/CalendarCompanies", CompanyBody(tenant, "Mars/Olympus")), HttpStatusCode.BadRequest, "CALENDAR_INVALID_TIME_ZONE");
        var created = await client.PostAsJsonAsync("/api/v1/CalendarCompanies", CompanyBody(tenant, "America/Managua"));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        await ShouldFailAsync(await client.PostAsJsonAsync("/api/v1/CalendarCompanies", CompanyBody(tenant, "America/Managua")), HttpStatusCode.Conflict, "CALENDAR_COMPANY_ALREADY_CONFIGURED");

        var mine = await ReadDataAsync(await client.GetAsync("/api/v1/UserCalendars/mine"));
        mine.GetProperty("pendingProvisioning").GetBoolean().Should().BeFalse();
        mine.GetProperty("calendars")[0].GetProperty("name").GetString().Should().Be("Mi calendario");

        var me = await ReadDataAsync(await client.GetAsync("/api/v1/CalendarUserConfigurations/me"));
        me.GetProperty("effectiveBusinessHoursStart").GetString().Should().StartWith("08:00");
        me.GetProperty("timeZone").GetString().Should().Be("America/Managua");

        var meeting = await GetUsableActivityIdAsync(client, "MEETING");

        // Monday 2027-03-01 10:00–11:00 local (16:00Z).
        var first = await client.PostAsJsonAsync("/api/v1/CalendarEvents", EventBody(meeting, "2027-03-01T10:00:00-06:00", "2027-03-01T11:00:00-06:00"));
        var firstBody = await first.Content.ReadAsStringAsync();
        first.StatusCode.Should().Be(HttpStatusCode.Created, firstBody);
        var firstData = Data(firstBody);
        var firstId = firstData.GetProperty("id").GetGuid();
        firstData.GetProperty("status").GetProperty("code").GetString().Should().Be("PENDING");
        firstData.GetProperty("startUtc").GetDateTime().Should().Be(new DateTime(2027, 3, 1, 16, 0, 0, DateTimeKind.Utc));
        firstData.GetProperty("origin").GetProperty("channelCode").GetString().Should().Be("WEB");

        // A pending activity blocks: overlap → 409; touching ends do not overlap.
        var overlap = await client.PostAsJsonAsync("/api/v1/CalendarEvents", EventBody(meeting, "2027-03-01T10:30:00-06:00", "2027-03-01T11:30:00-06:00"));
        var overlapBody = await ShouldFailAsync(overlap, HttpStatusCode.Conflict, "CALENDAR_EVENT_OVERLAP");
        overlapBody.Should().Contain(firstId.ToString());
        (await client.PostAsJsonAsync("/api/v1/CalendarEvents", EventBody(meeting, "2027-03-01T11:00:00-06:00", "2027-03-01T12:00:00-06:00")))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // Confirm.
        var confirmed = await StatusIdAsync(client, "CONFIRMED");
        var patch = await client.PatchAsJsonAsync($"/api/v1/CalendarEvents/{firstId}/status", new { calendarStatusCompanyId = confirmed });
        (await ReadDataAsync(patch)).GetProperty("status").GetProperty("code").GetString().Should().Be("CONFIRMED");

        // Cancelling requires a reason; a cancelled activity frees the slot and reactivating it revalidates.
        var cancelled = await StatusIdAsync(client, "CANCELLED");
        await ShouldFailAsync(await client.PatchAsJsonAsync($"/api/v1/CalendarEvents/{firstId}/status", new { calendarStatusCompanyId = cancelled }),
            HttpStatusCode.BadRequest, "CALENDAR_CANCELLATION_REASON_REQUIRED");
        (await client.PatchAsJsonAsync($"/api/v1/CalendarEvents/{firstId}/status", new { calendarStatusCompanyId = cancelled, notes = "Client cancelled" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var replacement = await client.PostAsJsonAsync("/api/v1/CalendarEvents", EventBody(meeting, "2027-03-01T10:00:00-06:00", "2027-03-01T11:00:00-06:00"));
        replacement.StatusCode.Should().Be(HttpStatusCode.Created);
        await ShouldFailAsync(await client.PatchAsJsonAsync($"/api/v1/CalendarEvents/{firstId}/status", new { calendarStatusCompanyId = confirmed }),
            HttpStatusCode.Conflict, "CALENDAR_EVENT_OVERLAP");

        // Week query: two active occurrences; with includeCancelled, three.
        var week = await ReadDataAsync(await client.GetAsync("/api/v1/CalendarEvents?from=2027-03-01T06:00:00Z&to=2027-03-08T06:00:00Z"));
        week.GetProperty("timeZone").GetString().Should().Be("America/Managua");
        week.GetProperty("occurrences").GetArrayLength().Should().Be(2);
        var withCancelled = await ReadDataAsync(await client.GetAsync("/api/v1/CalendarEvents?from=2027-03-01T06:00:00Z&to=2027-03-08T06:00:00Z&includeCancelled=true"));
        withCancelled.GetProperty("occurrences").GetArrayLength().Should().Be(3);
        await ShouldFailAsync(await client.GetAsync("/api/v1/CalendarEvents?from=2027-03-01T00:00:00Z&to=2027-06-01T00:00:00Z"), HttpStatusCode.BadRequest, "CALENDAR_RANGE_TOO_LARGE");

        // Log: Created, StatusChanged (confirm), StatusChanged (cancel).
        var logs = await ReadDataAsync(await client.GetAsync($"/api/v1/CalendarEvents/{firstId}/logs"));
        logs.EnumerateArray().Select(l => l.GetProperty("action").GetString()).Should().Equal("Created", "StatusChanged", "StatusChanged");

        // Every *Utc value round-trips as UTC (no Npgsql Kind errors).
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.CalendarEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == firstId);
        stored.StartUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Recurring_ExpandsAndEditsWithTheThreeScopes()
    {
        var tenant = await SeedTenantAsync(withModule: true);
        using var client = await CreateClientAsync(tenant, tenant.AdminEmail);
        await ConfigureAsync(client, tenant);
        var meeting = await GetUsableActivityIdAsync(client, "MEETING");

        // Every Monday 09:00–10:00, 6 times, from 2027-03-01.
        var series = await client.PostAsJsonAsync("/api/v1/CalendarEvents",
            EventBody(meeting, "2027-03-01T09:00:00-06:00", "2027-03-01T10:00:00-06:00", "FREQ=WEEKLY;BYDAY=MO;COUNT=6"));
        var seriesData = Data(await series.Content.ReadAsStringAsync());
        series.StatusCode.Should().Be(HttpStatusCode.Created, seriesData.ToString());
        var masterId = seriesData.GetProperty("id").GetGuid();
        seriesData.GetProperty("recurrenceEndUtc").GetDateTime().Should().Be(new DateTime(2027, 4, 5, 16, 0, 0, DateTimeKind.Utc));

        var month = await OccurrencesAsync(client, "2027-03-01T06:00:00Z", "2027-04-12T06:00:00Z");
        month.Should().HaveCount(6);

        // A one-time meeting against a later occurrence of the series → 409.
        await ShouldFailAsync(await client.PostAsJsonAsync("/api/v1/CalendarEvents", EventBody(meeting, "2027-03-22T09:30:00-06:00", "2027-03-22T10:30:00-06:00")),
            HttpStatusCode.Conflict, "CALENDAR_EVENT_OVERLAP");

        // Scope is required for a series.
        await ShouldFailAsync(await client.PutAsJsonAsync($"/api/v1/CalendarEvents/{masterId}", EventBody(meeting, "2027-03-08T11:00:00-06:00", "2027-03-08T12:00:00-06:00")),
            HttpStatusCode.BadRequest, "CALENDAR_RECURRENCE_SCOPE_REQUIRED");

        // ThisOccurrence: move 2027-03-08 to 11:00.
        var occurrenceKey = "2027-03-08T15:00:00Z";
        var moved = await client.PutAsJsonAsync($"/api/v1/CalendarEvents/{masterId}?scope=1&occurrenceStartUtc={occurrenceKey}",
            EventBody(meeting, "2027-03-08T11:00:00-06:00", "2027-03-08T12:00:00-06:00"));
        var movedData = Data(await moved.Content.ReadAsStringAsync());
        moved.StatusCode.Should().Be(HttpStatusCode.OK, movedData.ToString());
        movedData.GetProperty("seriesMasterId").GetGuid().Should().Be(masterId);
        movedData.GetProperty("status").GetProperty("code").GetString().Should().Be("PENDING", "the owner keeps the status when rescheduling");

        month = await OccurrencesAsync(client, "2027-03-01T06:00:00Z", "2027-04-12T06:00:00Z");
        month.Should().HaveCount(6);
        month.Single(o => o.GetProperty("exceptionId").ValueKind != JsonValueKind.Null)
            .GetProperty("startUtc").GetDateTime().Should().Be(new DateTime(2027, 3, 8, 17, 0, 0, DateTimeKind.Utc));

        // ThisAndFollowing from 2027-03-22: 10:00–11:00 from there on.
        var split = await client.PutAsJsonAsync($"/api/v1/CalendarEvents/{masterId}?scope=2&occurrenceStartUtc=2027-03-22T15:00:00Z",
            EventBody(meeting, "2027-03-22T10:00:00-06:00", "2027-03-22T11:00:00-06:00"));
        var splitData = Data(await split.Content.ReadAsStringAsync());
        split.StatusCode.Should().Be(HttpStatusCode.OK, splitData.ToString());
        splitData.GetProperty("recurrenceRule").GetString().Should().Be("FREQ=WEEKLY;BYDAY=MO;COUNT=3");

        month = await OccurrencesAsync(client, "2027-03-01T06:00:00Z", "2027-04-12T06:00:00Z");
        month.Should().HaveCount(6);
        month.Count(o => o.GetProperty("startUtc").GetDateTime().Hour == 15).Should().Be(2, "the first series keeps 03-01 and 03-15");
        month.Count(o => o.GetProperty("startUtc").GetDateTime().Hour == 17).Should().Be(1, "the 03-08 exception");
        month.Count(o => o.GetProperty("startUtc").GetDateTime().Hour == 16).Should().Be(3, "the new series from 03-22");

        // Series: delete the original series (with its exception) → only the new series remains.
        (await client.DeleteAsync($"/api/v1/CalendarEvents/{masterId}?scope=3&occurrenceStartUtc=2027-03-01T15:00:00Z")).StatusCode.Should().Be(HttpStatusCode.OK);
        month = await OccurrencesAsync(client, "2027-03-01T06:00:00Z", "2027-04-12T06:00:00Z");
        month.Should().HaveCount(3);

        // Invalid rule.
        await ShouldFailAsync(await client.PostAsJsonAsync("/api/v1/CalendarEvents",
                EventBody(meeting, "2027-05-03T09:00:00-06:00", "2027-05-03T10:00:00-06:00", "FREQ=HOURLY")),
            HttpStatusCode.BadRequest, "CALENDAR_INVALID_RECURRENCE_RULE");
    }

    [Fact]
    public async Task Panel_AppliesBusinessHoursRescheduleStatusAvailabilityAndPeriods()
    {
        var tenant = await SeedTenantAsync(withModule: true);
        using var client = await CreateClientAsync(tenant, tenant.AdminEmail);
        await ConfigureAsync(client, tenant);
        var meeting = await GetUsableActivityIdAsync(client, "MEETING");
        var reminder = await GetUsableActivityIdAsync(client, "REMINDER");

        // Outside the owner's business hours (07:00) → 422; on a weekend → 422; a reminder does not care.
        await ShouldFailAsync(await client.PostAsJsonAsync("/api/v1/CalendarPanel/events",
                PanelBody(tenant.OtherUserId, meeting, "2027-03-02T07:00:00-06:00", "2027-03-02T08:00:00-06:00")),
            HttpStatusCode.UnprocessableEntity, "CALENDAR_OUTSIDE_BUSINESS_HOURS");
        await ShouldFailAsync(await client.PostAsJsonAsync("/api/v1/CalendarPanel/events",
                PanelBody(tenant.OtherUserId, meeting, "2027-03-06T10:00:00-06:00", "2027-03-06T11:00:00-06:00")),
            HttpStatusCode.UnprocessableEntity, "CALENDAR_OUTSIDE_WORK_DAYS");
        (await client.PostAsJsonAsync("/api/v1/CalendarPanel/events",
                PanelBody(tenant.OtherUserId, reminder, "2027-03-06T07:00:00-06:00", null)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // Holiday (recurring) and non-working period.
        (await client.PostAsJsonAsync("/api/v1/CalendarCompanyNonWorkingDays", new { date = "2026-03-03", name = "Feriado", isRecurringYearly = true }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        await ShouldFailAsync(await client.PostAsJsonAsync("/api/v1/CalendarPanel/events",
                PanelBody(tenant.OtherUserId, meeting, "2027-03-03T10:00:00-06:00", "2027-03-03T11:00:00-06:00")),
            HttpStatusCode.UnprocessableEntity, "CALENDAR_COMPANY_HOLIDAY");

        var inside = await client.PostAsJsonAsync("/api/v1/CalendarPanel/events",
            PanelBody(tenant.OtherUserId, meeting, "2027-03-04T10:00:00-06:00", "2027-03-04T11:00:00-06:00"));
        var insideData = Data(await inside.Content.ReadAsStringAsync());
        inside.StatusCode.Should().Be(HttpStatusCode.Created, insideData.ToString());
        var otherEventId = insideData.GetProperty("id").GetGuid();
        insideData.GetProperty("ownerUserId").GetGuid().Should().Be(tenant.OtherUserId);

        var period = await client.PostAsJsonAsync("/api/v1/CalendarUserNonWorkingPeriods",
            new { userId = tenant.OtherUserId, startDate = "2027-03-04", endDate = "2027-03-05", reason = 1 });
        var periodData = Data(await period.Content.ReadAsStringAsync());
        period.StatusCode.Should().Be(HttpStatusCode.Created, periodData.ToString());
        periodData.GetProperty("affectedEvents").GetArrayLength().Should().Be(1);
        await ShouldFailAsync(await client.PostAsJsonAsync("/api/v1/CalendarPanel/events",
                PanelBody(tenant.OtherUserId, meeting, "2027-03-05T10:00:00-06:00", "2027-03-05T11:00:00-06:00")),
            HttpStatusCode.UnprocessableEntity, "CALENDAR_USER_NOT_AVAILABLE");

        // Rescheduled by a non-owner → the company's reschedule status.
        var rescheduled = await client.PutAsJsonAsync($"/api/v1/CalendarPanel/events/{otherEventId}",
            EventBody(meeting, "2027-03-08T14:00:00-06:00", "2027-03-08T15:00:00-06:00"));
        var rescheduledData = Data(await rescheduled.Content.ReadAsStringAsync());
        rescheduled.StatusCode.Should().Be(HttpStatusCode.OK, rescheduledData.ToString());
        rescheduledData.GetProperty("status").GetProperty("code").GetString().Should().Be("RESCHEDULED");

        // Availability: Monday 2027-03-08, 60-minute blocks from 08:00 to 17:00 minus the 14:00 meeting → 8.
        var availability = await ReadDataAsync(await client.GetAsync(
            $"/api/v1/CalendarPanel/availability?userId={tenant.OtherUserId}&from=2027-03-08T06:00:00Z&to=2027-03-09T06:00:00Z&activityCompanyId={meeting}"));
        availability.GetProperty("slots").GetArrayLength().Should().Be(8);

        // Panel events show the other user's period as Unavailable, without reason.
        var panelEvents = await ReadDataAsync(await client.GetAsync(
            $"/api/v1/CalendarPanel/events?userIds={tenant.OtherUserId}&from=2027-03-01T06:00:00Z&to=2027-03-15T06:00:00Z"));
        panelEvents.GetProperty("nonWorkingDays").EnumerateArray()
            .Should().Contain(d => d.GetProperty("kind").GetString() == "Unavailable" && d.GetProperty("name").ValueKind == JsonValueKind.Null);

        // The own calendar endpoints do not reveal another user's activity (404, not 403).
        await ShouldFailAsync(await client.GetAsync($"/api/v1/CalendarEvents/{otherEventId}"), HttpStatusCode.NotFound, "CALENDAR_EVENT_NOT_FOUND");

        // A user of another company is rejected.
        await ShouldFailAsync(await client.PostAsJsonAsync("/api/v1/CalendarPanel/events",
                PanelBody(Guid.NewGuid(), meeting, "2027-03-09T10:00:00-06:00", "2027-03-09T11:00:00-06:00")),
            HttpStatusCode.BadRequest, "CALENDAR_USER_NOT_IN_COMPANY");
    }

    [Fact]
    public async Task Settings_StatusAndActivityBlocksAndUserHours()
    {
        var tenant = await SeedTenantAsync(withModule: true);
        using var client = await CreateClientAsync(tenant, tenant.AdminEmail);
        await ConfigureAsync(client, tenant);

        var pending = await StatusIdAsync(client, "PENDING");
        await ShouldFailAsync(await client.DeleteAsync($"/api/v1/CalendarStatusCompanies/{pending}"), HttpStatusCode.Conflict, "CALENDAR_STATUS_REQUIRED_BY_SYSTEM");
        var rescheduled = await StatusIdAsync(client, "RESCHEDULED");
        await ShouldFailAsync(await client.DeleteAsync($"/api/v1/CalendarStatusCompanies/{rescheduled}"), HttpStatusCode.Conflict, "CALENDAR_STATUS_IN_USE_AS_RESCHEDULE");

        // User hours outside the company hours → 409; inside → 200; then narrowing the company hours → 409.
        await ShouldFailAsync(await client.PutAsJsonAsync($"/api/v1/CalendarUserConfigurations/{tenant.OtherUserId}",
                new { businessHoursStart = "07:00:00", businessHoursEnd = "12:00:00", workDays = 62, defaultView = 2, weekStartsOn = 1 }),
            HttpStatusCode.Conflict, "CALENDAR_USER_HOURS_OUT_OF_COMPANY_RANGE");
        (await client.PutAsJsonAsync($"/api/v1/CalendarUserConfigurations/{tenant.OtherUserId}",
                new { businessHoursStart = "08:00:00", businessHoursEnd = "12:00:00", workDays = 62, defaultView = 2, weekStartsOn = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await ShouldFailAsync(await client.PutAsJsonAsync("/api/v1/CalendarCompanies/current",
                new { defaultActivityUserId = tenant.AdminUserId, timeZone = "America/Managua", isOpen24Hours = false, businessHoursStart = "09:00:00", businessHoursEnd = "17:00:00" }),
            HttpStatusCode.Conflict, "CALENDAR_USER_HOURS_OUT_OF_COMPANY_RANGE");

        // The default calendar cannot be deleted.
        var mine = await ReadDataAsync(await client.GetAsync("/api/v1/UserCalendars/mine"));
        var defaultCalendar = mine.GetProperty("calendars")[0].GetProperty("id").GetGuid();
        await ShouldFailAsync(await client.DeleteAsync($"/api/v1/UserCalendars/{defaultCalendar}"), HttpStatusCode.Conflict, "CALENDAR_IS_DEFAULT");

        // A second calendar can become the default.
        var second = Data(await (await client.PostAsJsonAsync("/api/v1/UserCalendars", new { name = "Ventas", color = "#43A047" })).Content.ReadAsStringAsync());
        var setDefault = await ReadDataAsync(await client.PutAsync($"/api/v1/UserCalendars/{second.GetProperty("id").GetGuid()}/set-default", null));
        setDefault.GetProperty("isDefault").GetBoolean().Should().BeTrue();
        (await client.DeleteAsync($"/api/v1/UserCalendars/{defaultCalendar}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Sync is idempotent once the catalogs exist.
        var sync = await ReadDataAsync(await client.PostAsync("/api/v1/CalendarCompanies/current/sync-catalogs", null));
        sync.GetProperty("activitiesAdded").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Provisioning_WithoutModuleCreatesNothing()
    {
        var tenant = await SeedTenantAsync(withModule: false);
        using var client = await CreateClientAsync(tenant, tenant.AdminEmail);
        await ConfigureAsync(client, tenant);

        var current = await ReadDataAsync(await client.GetAsync("/api/v1/CalendarCompanies/current"));
        current.GetProperty("moduleEnabled").GetBoolean().Should().BeFalse();
        current.GetProperty("usersWithoutCalendar").GetInt32().Should().Be(2);
        var mine = await ReadDataAsync(await client.GetAsync("/api/v1/UserCalendars/mine"));
        mine.GetProperty("pendingProvisioning").GetBoolean().Should().BeTrue();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private sealed record Tenant(Guid CompanyId, Guid AdminUserId, string AdminEmail, Guid OtherUserId);

    private static object CompanyBody(Tenant tenant, string timeZone) => new
    {
        defaultActivityUserId = tenant.AdminUserId,
        timeZone,
        isOpen24Hours = false,
        businessHoursStart = "08:00:00",
        businessHoursEnd = "17:00:00"
    };

    private static object EventBody(Guid activity, string start, string? end, string? rule = null) => new
    {
        calendarActivityCompanyId = activity,
        title = "Revisión de contrato",
        isAllDay = false,
        start,
        end,
        recurrenceRule = rule
    };

    private static object PanelBody(Guid owner, Guid activity, string start, string? end) => new
    {
        ownerUserId = owner,
        calendarActivityCompanyId = activity,
        title = "Reunión desde el panel",
        isAllDay = false,
        start,
        end
    };

    private async Task ConfigureAsync(HttpClient client, Tenant tenant)
    {
        var response = await client.PostAsJsonAsync("/api/v1/CalendarCompanies", CompanyBody(tenant, "America/Managua"));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> GetUsableActivityIdAsync(HttpClient client, string code)
    {
        var usable = await ReadDataAsync(await client.GetAsync("/api/v1/CalendarActivityCompanies/usable"));
        return usable.EnumerateArray().Single(a => a.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> StatusIdAsync(HttpClient client, string code)
    {
        var usable = await ReadDataAsync(await client.GetAsync("/api/v1/CalendarStatusCompanies/usable"));
        return usable.EnumerateArray().Single(s => s.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();
    }

    private static async Task<List<JsonElement>> OccurrencesAsync(HttpClient client, string from, string to)
    {
        var data = await ReadDataAsync(await client.GetAsync($"/api/v1/CalendarEvents?from={from}&to={to}"));
        return data.GetProperty("occurrences").EnumerateArray().ToList();
    }

    private static async Task<string> ShouldFailAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("message").GetString().Should().Be(code, body);
        return body;
    }

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
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

    /// <summary>
    /// A company with the global calendar catalogs copied, the Calendar module (optional) and two members: the
    /// admin (SuperAdmin, used by the client) and another user (the owner in the panel cases).
    /// </summary>
    private async Task<Tenant> SeedTenantAsync(bool withModule)
    {
        Guid companyId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var company = new Company
            {
                Name = $"JOIN-S44-{Guid.NewGuid():N}"[..20],
                TaxId = $"RUC{Guid.NewGuid():N}"[..16],
                IsActive = true,
                GcRecord = BaseAuditableEntity.ActiveGcRecord,
                CreatedBy = Creator
            };
            db.Companies.Add(company);

            if (withModule)
            {
                var moduleId = await db.SystemModules.IgnoreQueryFilters().Where(m => m.Name == "Calendar").Select(m => m.Id).SingleAsync();
                db.CompanyModules.Add(new CompanyModule { CompanyId = company.Id, ModuleId = moduleId, IsActive = true, CreatedBy = Creator });
            }

            await db.SaveChangesAsync();
            companyId = company.Id;

            var seeder = scope.ServiceProvider.GetRequiredService<ICompanyCatalogSeeder>();
            await seeder.SeedCalendarCatalogsForCompanyAsync(companyId);
        }

        var adminEmail = $"a{Guid.NewGuid():N}@t.local";
        var otherEmail = $"o{Guid.NewGuid():N}@t.local";
        var adminId = await RegisterMemberAsync(companyId, adminEmail, "SuperAdmin");
        var otherId = await RegisterMemberAsync(companyId, otherEmail, null);
        return new Tenant(companyId, adminId, adminEmail, otherId);
    }

    private async Task<Guid> RegisterMemberAsync(Guid companyId, string email, string? role)
    {
        using var unauthClient = _factory.CreateClient();
        (await unauthClient.PostAsJsonAsync("/api/v1/users/register", new RegisterCommand
        {
            Email = email,
            Password = Password,
            FirstName = "Spec44",
            LastName = role ?? "Member",
        })).EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull();

        db.UserCompanies.Add(new UserCompany { UserId = user!.Id, CompanyId = companyId, IsDefault = true, CreatedBy = Creator });
        if (role is not null)
        {
            (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
        }

        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<HttpClient> CreateClientAsync(Tenant tenant, string email)
    {
        using var unauthClient = _factory.CreateClient();
        var login = await unauthClient.PostAsJsonAsync("/api/v1/users/login", new LoginCommand { Email = email, Password = Password });
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<Response<LoginResponse>>();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Data!.Token);
        client.DefaultRequestHeaders.Add("X-Company-Id", tenant.CompanyId.ToString());
        return client;
    }
}
