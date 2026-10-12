using FluentAssertions;
using JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Queries.GetCalendarEventById;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Queries.GetCalendarEventLogs;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Queries.GetCalendarEventsRange;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Queries.GetUpcomingCalendarEvents;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelAvailability;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelCalendars;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelEventById;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelEventLogs;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelEvents;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelUsers;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using static JOIN.Application.UnitTest.UseCases.Calendars.Queries.CalendarQueryHandlersTests;
using static JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles.CalendarTestContext;

namespace JOIN.Application.UnitTest.UseCases.Calendars.Queries;

/// <summary>
/// SPEC 44 — range, upcoming, detail, log and panel queries: one-time activities, series expanded with exceptions
/// applied, filters, holidays and non-working periods (own with reason, others as <c>Unavailable</c>).
/// </summary>
public sealed class CalendarEventQueryHandlersTests
{
    private static Dictionary<string, object?> EventRow(Guid id, Guid owner, DateTime start, DateTime end, string? rule = null,
        Guid? master = null, DateTime? originalStart = null, string status = "PENDING", bool allowsOverlap = false, bool blocksTime = true,
        bool isFinal = false, Guid? calendarId = null, Guid? activityId = null) => Row(
        ("Id", id), ("SeriesMasterId", master), ("OriginalStartUtc", originalStart), ("CalendarId", calendarId ?? Guid.Empty),
        ("CalendarName", "Mi calendario"), ("CalendarColor", "#1E88E5"), ("OwnerUserId", owner), ("OwnerName", " Owner User "),
        ("Title", "Reunión"), ("IsAllDay", false), ("StartUtc", start), ("EndUtc", end), ("RecurrenceRule", rule),
        ("ActivityCompanyId", activityId ?? Guid.Empty), ("ActivityCode", "MEETING"), ("ActivityName", "Reunión"), ("ActivityColor", "#000000"),
        ("AllowsOverlap", allowsOverlap), ("StatusCompanyId", Guid.Empty), ("StatusCode", status), ("StatusName", status), ("StatusColor", "#111111"),
        ("BlocksTime", blocksTime), ("IsFinal", isFinal), ("ChannelCode", "WEB"), ("OriginType", 1));

    [Fact]
    public async Task Range_ShouldExpandSeriesApplyExceptionsAndFilterCancelled()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var master = Guid.NewGuid();
        var oneTime = Guid.NewGuid();
        var cancelled = Guid.NewGuid();
        var exception = Guid.NewGuid();
        ctx.Connection
            .Then(CompanyRow())
            .Then(FakeResultSet.FromRows(
                EventRow(master, ctx.OwnerId, Utc(2027, 3, 1, 9), Utc(2027, 3, 1, 10), "FREQ=DAILY;COUNT=5"),
                EventRow(oneTime, ctx.OwnerId, Utc(2027, 3, 2, 14), Utc(2027, 3, 2, 15)),
                EventRow(cancelled, ctx.OwnerId, Utc(2027, 3, 3, 14), Utc(2027, 3, 3, 15), status: "CANCELLED", blocksTime: false, isFinal: true)))
            .Then(FakeResultSet.FromRows(EventRow(exception, ctx.OwnerId, Utc(2027, 3, 2, 11), Utc(2027, 3, 2, 12), master: master, originalStart: Utc(2027, 3, 2, 9))))
            .Then(FakeResultSet.FromRows(Row(("Id", Guid.NewGuid()), ("Date", new DateOnly(2027, 3, 4)), ("Name", "Feriado"), ("IsRecurringYearly", false))))
            .Then(FakeResultSet.FromRows(
                Row(("Id", Guid.NewGuid()), ("UserId", ctx.OwnerId), ("StartDate", new DateOnly(2027, 3, 5)), ("EndDate", new DateOnly(2027, 3, 5)), ("Reason", 1))));

        var response = await ctx.SendAsync(new GetCalendarEventsRangeQuery(Local(2027, 3, 1, 0), Local(2027, 3, 8, 0)));

        response.IsSuccess.Should().BeTrue(response.Message);
        var occurrences = response.Data!.Occurrences;
        occurrences.Should().HaveCount(6, "five series occurrences (one replaced by its exception) and one one-time activity");
        occurrences.Should().BeInAscendingOrder(o => o.StartUtc);
        var replaced = occurrences.Single(o => o.ExceptionId == exception);
        replaced.EventId.Should().Be(master);
        replaced.OccurrenceStartUtc.Should().Be(Utc(2027, 3, 2, 9));
        replaced.IsRecurring.Should().BeTrue();
        occurrences.Single(o => o.EventId == oneTime).IsRecurring.Should().BeFalse();
        occurrences.Should().NotContain(o => o.EventId == cancelled);
        occurrences[0].OwnerName.Should().Be("Owner User");
        occurrences[0].Origin.Type.Should().Be("User");
        response.Data.NonWorkingDays.Should().Contain(d => d.Kind == "Holiday" && d.Name == "Feriado");
        response.Data.NonWorkingDays.Should().Contain(d => d.Kind == "NonWorkingPeriod" && d.Name == "Vacation");
        ctx.Connection.Commands[1].Should().Contain("e.CompanyId = @TenantId").And.Contain("e.GcRecord = 0").And.Contain("e.OwnerUserId = ANY(");
        ctx.Connection.Commands[2].Should().Contain("e.SeriesMasterId = ANY(");
    }

    [Fact]
    public async Task Range_WithFilters_ShouldKeepMatchingOccurrencesOnly()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var keepCalendar = Guid.NewGuid();
        var keepActivity = Guid.NewGuid();
        ctx.Connection
            .Then(CompanyRow())
            .Then(FakeResultSet.FromRows(
                EventRow(Guid.NewGuid(), ctx.OwnerId, Utc(2027, 3, 1, 9), Utc(2027, 3, 1, 10), calendarId: keepCalendar, activityId: keepActivity),
                EventRow(Guid.NewGuid(), ctx.OwnerId, Utc(2027, 3, 1, 11), Utc(2027, 3, 1, 12), calendarId: Guid.NewGuid(), activityId: keepActivity),
                EventRow(Guid.NewGuid(), ctx.OwnerId, Utc(2027, 3, 1, 13), Utc(2027, 3, 1, 14), status: "CANCELLED", blocksTime: false, calendarId: keepCalendar, activityId: keepActivity)))
            .Then(NoRows("Id", "Date"))
            .Then(NoRows("Id", "UserId"));

        var response = await ctx.SendAsync(new GetCalendarEventsRangeQuery(Local(2027, 3, 1, 0), Local(2027, 3, 2, 0),
            [keepCalendar], [keepActivity], [Guid.Empty], IncludeCancelled: true));

        response.Data!.Occurrences.Should().HaveCount(2);
    }

    [Fact]
    public async Task Range_ShouldValidateTheRangeAndTheConfiguration()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        ctx.Connection.Then(NoRows("Id", "TimeZone"));

        (await ctx.SendAsync(new GetCalendarEventsRangeQuery(Local(2027, 3, 1, 0), Local(2027, 6, 1, 0)))).Message.Should().Be(CalendarErrorCodes.RangeTooLarge);
        (await ctx.SendAsync(new GetCalendarEventsRangeQuery(Local(2027, 3, 2, 0), Local(2027, 3, 1, 0)))).Message.Should().Be(CalendarErrorCodes.RangeTooLarge);
        (await ctx.SendAsync(new GetCalendarEventsRangeQuery(Local(2027, 3, 1, 0), Local(2027, 3, 2, 0)))).Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
        (await ctx.SendAsync(new GetUpcomingCalendarEventsQuery(0))).Message.Should().Be(CalendarErrorCodes.RangeTooLarge);

        ctx.ActingUserId = null;
        (await ctx.SendAsync(new GetCalendarEventsRangeQuery(Local(2027, 3, 1, 0), Local(2027, 3, 2, 0)))).Message.Should().Be(CalendarErrorCodes.UserRequired);
        (await ctx.SendAsync(new GetUpcomingCalendarEventsQuery())).Message.Should().Be(CalendarErrorCodes.UserRequired);
        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(new GetCalendarEventByIdQuery(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetCalendarEventLogsQuery(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task Upcoming_ShouldExcludeFinalStatuses()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var now = DateTime.UtcNow;
        ctx.Connection
            .Then(CompanyRow())
            .Then(FakeResultSet.FromRows(
                EventRow(Guid.NewGuid(), ctx.OwnerId, now.AddHours(2), now.AddHours(3)),
                EventRow(Guid.NewGuid(), ctx.OwnerId, now.AddHours(4), now.AddHours(5), status: "COMPLETED", isFinal: true)))
            .Then(NoRows("Id", "Date"))
            .Then(NoRows("Id", "UserId"));

        var response = await ctx.SendAsync(new GetUpcomingCalendarEventsQuery(3));

        response.Data!.Occurrences.Should().ContainSingle(o => o.Status.Code == "PENDING");
    }

    [Fact]
    public async Task ById_AndLogs_ShouldHideOtherUsersActivitiesInTheOwnCalendar()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var id = Guid.NewGuid();
        var other = EventRow(id, ctx.OtherId, Utc(2027, 3, 1, 9), Utc(2027, 3, 1, 10), "FREQ=WEEKLY;BYDAY=MO;COUNT=2");
        other["Description"] = "detalle";
        other["CreatedAt"] = DateTime.UtcNow;
        var log = Row(("Id", Guid.NewGuid()), ("CalendarEventId", id), ("Action", (int)CalendarEventLogAction.StatusChanged), ("ChannelCode", "WEB"),
            ("ActorName", " Owner User "), ("NewStatusName", "confirmed"), ("Notes", "ok"), ("OccurredUtc", DateTime.UtcNow));
        ctx.Connection
            .Then(CompanyRow()).Then(FakeResultSet.FromRows(other))
            .Then(CompanyRow()).Then(FakeResultSet.FromRows(other))
            .Then(FakeResultSet.FromRows(other))
            .Then(FakeResultSet.FromRows(other)).Then(FakeResultSet.FromRows(log))
            .Then(NoRows("Id", "TimeZone"));

        (await ctx.SendAsync(new GetCalendarEventByIdQuery(id))).Message.Should().Be(CalendarErrorCodes.EventNotFound);

        var panel = await ctx.SendAsync(new GetCalendarPanelEventByIdQuery(id));
        panel.Data!.RecurrenceDescription.Should().Be("Cada semana: lunes; 2 veces");
        panel.Data.Description.Should().Be("detalle");
        panel.Data.OwnerName.Should().Be("Owner User");

        (await ctx.SendAsync(new GetCalendarEventLogsQuery(id))).Message.Should().Be(CalendarErrorCodes.EventNotFound);
        var logs = await ctx.SendAsync(new GetCalendarPanelEventLogsQuery(id));
        logs.Data!.Single().Action.Should().Be("StatusChanged");
        logs.Data!.Single().ActorName.Should().Be("Owner User");

        (await ctx.SendAsync(new GetCalendarEventByIdQuery(id))).Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
    }

    [Fact]
    public async Task PanelEvents_ShouldLimitUsersAndHideOtherUsersReasons()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        ctx.Connection
            .Then(CompanyRow())
            .Then(FakeResultSet.FromRows(EventRow(Guid.NewGuid(), ctx.OtherId, Utc(2027, 3, 1, 9), Utc(2027, 3, 1, 10))))
            .Then(NoRows("Id", "Date"))
            .Then(FakeResultSet.FromRows(Row(("Id", Guid.NewGuid()), ("UserId", ctx.OtherId), ("StartDate", new DateOnly(2027, 3, 2)), ("EndDate", new DateOnly(2027, 3, 2)), ("Reason", 2))));

        (await ctx.SendAsync(new GetCalendarPanelEventsQuery(Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToArray(), Local(2027, 3, 1, 0), Local(2027, 3, 2, 0))))
            .Message.Should().Be(CalendarErrorCodes.TooManyUsers);

        var response = await ctx.SendAsync(new GetCalendarPanelEventsQuery([ctx.OtherId, ctx.OtherId], Local(2027, 3, 1, 0), Local(2027, 3, 8, 0)));

        response.Data!.Occurrences.Should().ContainSingle(o => o.OwnerUserId == ctx.OtherId);
        response.Data.NonWorkingDays.Should().ContainSingle(d => d.Kind == "Unavailable" && d.Name == null && d.UserId == ctx.OtherId);
    }

    [Fact]
    public async Task PanelUsersAndCalendars_ShouldBeScopedToTheCompany()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        ctx.Connection
            .Then(FakeResultSet.FromRows(Row(("UserId", ctx.OtherId), ("Name", " Other User "), ("Email", "other@t.local"), ("HasCalendar", true))), FakeResultSet.FromScalar(1))
            .Then(FakeResultSet.FromRows(Row(("Id", Guid.NewGuid()), ("OwnerUserId", ctx.OtherId), ("OwnerName", " Other "), ("Name", "Mi calendario"), ("IsDefault", true))));

        var users = await ctx.SendAsync(new GetCalendarPanelUsersQuery(" oth ", true));
        users.Data!.Items.Single().Name.Should().Be("Other User");
        ctx.Connection.LastCommandText.Should().Contain("c.Id IS NOT NULL").And.Contain("uc.CompanyId = @TenantId");

        var calendars = await ctx.SendAsync(new GetCalendarPanelCalendarsQuery([ctx.OtherId]));
        calendars.Data!.Single().OwnerName.Should().Be("Other");
        (await ctx.SendAsync(new GetCalendarPanelCalendarsQuery([]))).Message.Should().Be(CalendarErrorCodes.TooManyUsers);

        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(new GetCalendarPanelUsersQuery())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetCalendarPanelCalendarsQuery([ctx.OtherId]))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetCalendarPanelAvailabilityQuery(ctx.OtherId, Local(2027, 3, 1, 0), Local(2027, 3, 2, 0), Guid.NewGuid())))
            .Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task Availability_ShouldSkipBusySlotsAndReportErrors()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var user = Row(("ConfigurationId", Guid.NewGuid()), ("WorkDays", 62));
        var meeting = Row(("DefaultDurationMinutes", 60), ("AllowsOverlap", false), ("RequiresBusinessHours", true));
        ctx.Connection
            .Then(CompanyRow()).Then(FakeResultSet.FromRows(user)).Then(FakeResultSet.FromRows(meeting))
            .Then(FakeResultSet.FromRows(EventRow(Guid.NewGuid(), ctx.OtherId, Utc(2027, 3, 1, 10), Utc(2027, 3, 1, 11))))
            .Then(NoRows("Id", "UserId"))
            .Then(NoRows("Id", "Date"))
            .Then(NoRows("Id", "TimeZone"))
            .Then(CompanyRow()).Then(NoRows("ConfigurationId"))
            .Then(CompanyRow()).Then(FakeResultSet.FromRows(Row(("WorkDays", 0))))
            .Then(CompanyRow()).Then(FakeResultSet.FromRows(user)).Then(NoRows("DefaultDurationMinutes"));

        var query = new GetCalendarPanelAvailabilityQuery(ctx.OtherId, Local(2027, 3, 1, 0), Local(2027, 3, 2, 0), ctx.Activity[CalendarCodes.Activities.Meeting]);
        var response = await ctx.SendAsync(query);

        response.Data!.Slots.Should().HaveCount(8, "08:00–17:00 in one-hour blocks minus the 10:00 meeting");
        response.Data.Slots.Should().NotContain(s => s.StartUtc == Utc(2027, 3, 1, 10));
        response.Data.DurationMinutes.Should().Be(60);

        (await ctx.SendAsync(query with { To = Local(2027, 3, 20, 0) })).Message.Should().Be(CalendarErrorCodes.RangeTooLarge);
        (await ctx.SendAsync(query)).Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
        (await ctx.SendAsync(query)).Message.Should().Be(CalendarErrorCodes.UserNotInCompany);
        (await ctx.SendAsync(query)).Message.Should().Be(CalendarErrorCodes.UserHasNoCalendar);
        (await ctx.SendAsync(query)).Message.Should().Be(CalendarErrorCodes.ActivityNotEnabled);
    }
}
