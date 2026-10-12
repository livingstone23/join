using FluentAssertions;
using JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.ChangeCalendarEventStatus;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.CreateCalendarEvent;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.DeleteCalendarEvent;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.UpdateCalendarEvent;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.ChangeCalendarPanelEventStatus;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.CreateCalendarPanelEvent;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.DeleteCalendarPanelEvent;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.UpdateCalendarPanelEvent;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Moq;
using static JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles.CalendarTestContext;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEvents;

/// <summary>
/// SPEC 44 — activities (R3–R9) through the <c>CalendarEvents</c> and <c>CalendarPanel</c> command handlers and
/// <see cref="CalendarEventCommandProcessor"/>, over an InMemory database. Dates: March 2027, America/Managua (UTC-6);
/// 2027-03-01 is a Monday.
/// </summary>
public sealed class CalendarEventCommandsTests
{
    private static CreateCalendarEventCommand Meeting(CalendarTestContext ctx, DateTimeOffset start, DateTimeOffset? end, string? rule = null, string code = CalendarCodes.Activities.Meeting) => new()
    {
        CalendarActivityCompanyId = ctx.Activity[code],
        Title = "  Reunión  ",
        Summary = " resumen ",
        Start = start,
        End = end,
        RecurrenceRule = rule
    };

    private static UpdateCalendarEventCommand Update(CalendarTestContext ctx, Guid id, DateTimeOffset start, DateTimeOffset end,
        CalendarRecurrenceEditScope? scope = null, DateTime? occurrence = null, string? rule = null, string code = CalendarCodes.Activities.Meeting) => new()
    {
        Id = id,
        Scope = scope,
        OccurrenceStartUtc = occurrence,
        CalendarActivityCompanyId = ctx.Activity[code],
        Title = "Editada",
        Start = start,
        End = end,
        RecurrenceRule = rule
    };

    private static async Task<Guid> NewEventAsync(CalendarTestContext ctx, CreateCalendarEventCommand command)
    {
        var response = await ctx.SendAsync(command);
        response.IsSuccess.Should().BeTrue(response.Message + string.Join(" ", response.Errors ?? []));
        return response.Data!.Id;
    }

    // ---------------------------------------------------------------- create

    [Fact]
    public async Task Create_ShouldUseInitialStatusDefaultCalendarAndWebChannel()
    {
        using var ctx = await CalendarTestContext.CreateAsync();

        var response = await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11)));

        response.IsSuccess.Should().BeTrue();
        var dto = response.Data!;
        dto.Status.Code.Should().Be(CalendarCodes.Statuses.Pending);
        dto.Activity.Code.Should().Be(CalendarCodes.Activities.Meeting);
        dto.CalendarId.Should().Be(ctx.DefaultCalendar[ctx.OwnerId]);
        dto.Title.Should().Be("Reunión");
        dto.Summary.Should().Be("resumen");
        dto.StartUtc.Should().Be(Utc(2027, 3, 1, 10));
        dto.StartUtc.Kind.Should().Be(DateTimeKind.Utc);
        dto.Origin.ChannelCode.Should().Be("WEB");
        dto.Origin.Type.Should().Be("User");
        dto.TimeZone.Should().Be("America/Managua");
        dto.OwnerName.Should().Be("Owner User");

        var logs = await ctx.LogsAsync(dto.Id);
        logs.Should().ContainSingle(l => l.Action == CalendarEventLogAction.Created && l.ChannelId == ctx.WebChannelId && l.ActorUserId == ctx.OwnerId);
    }

    [Fact]
    public async Task Create_WithoutEnd_ShouldUseTheCompanyTypeDuration()
    {
        using var ctx = await CalendarTestContext.CreateAsync();

        var response = await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 9), null, code: CalendarCodes.Activities.Task));

        response.Data!.EndUtc.Should().Be(Utc(2027, 3, 1, 9, 30));
    }

    [Fact]
    public async Task Create_AllDay_ShouldGoFromLocalMidnightToTheNextLocalMidnight()
    {
        using var ctx = await CalendarTestContext.CreateAsync();

        var response = await ctx.SendAsync(new CreateCalendarEventCommand
        {
            CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.Reminder],
            Title = "Inventario",
            IsAllDay = true,
            StartDate = new DateOnly(2027, 3, 1),
            EndDate = new DateOnly(2027, 3, 2)
        });

        response.Data!.StartUtc.Should().Be(new DateTime(2027, 3, 1, 6, 0, 0, DateTimeKind.Utc));
        response.Data.EndUtc.Should().Be(new DateTime(2027, 3, 3, 6, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData(11, 10, "The end must be later")]
    [InlineData(10, null, null)]
    public async Task Create_WithInvalidTimes_ShouldFail(int startHour, int? endHour, string? message)
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var command = Meeting(ctx, Local(2027, 3, 1, startHour), endHour is null ? null : Local(2027, 3, 1, endHour.Value));

        if (message is null)
        {
            command = command with { End = Local(2027, 3, 20, 10) };
        }

        var response = await ctx.SendAsync(command);

        response.Message.Should().Be(CalendarErrorCodes.InvalidEventTimes);
    }

    [Fact]
    public async Task Create_AllDayWithEndBeforeStart_ShouldFail()
    {
        using var ctx = await CalendarTestContext.CreateAsync();

        var response = await ctx.SendAsync(new CreateCalendarEventCommand
        {
            CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.Reminder],
            Title = "x",
            IsAllDay = true,
            StartDate = new DateOnly(2027, 3, 2),
            EndDate = new DateOnly(2027, 3, 1)
        });

        response.Message.Should().Be(CalendarErrorCodes.InvalidEventTimes);
    }

    [Fact]
    public async Task Create_WithDisabledType_ShouldReturnActivityNotEnabled()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var type = await ctx.Db.CalendarActivities.SingleAsync(a => a.Id == ctx.GlobalActivity[CalendarCodes.Activities.Meeting]);
        type.IsActive = false;
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        var response = await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11)));

        response.Message.Should().Be(CalendarErrorCodes.ActivityNotEnabled);
    }

    [Fact]
    public async Task Create_WithInactiveInitialStatus_ShouldReturnStatusNotEnabled()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var pending = await ctx.Db.CalendarStatusCompanies.SingleAsync(s => s.Id == ctx.Status[CalendarCodes.Statuses.Pending]);
        pending.IsActive = false;
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        var response = await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11)));

        response.Message.Should().Be(CalendarErrorCodes.StatusNotEnabled);
    }

    [Fact]
    public async Task Create_WithoutCompanyOrUserOrConfiguration_ShouldFail()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 10), null))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);

        ctx.ActingCompanyId = ctx.CompanyId;
        ctx.ActingUserId = null;
        (await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 10), null))).Message.Should().Be(CalendarErrorCodes.UserRequired);

        using var unconfigured = await CalendarTestContext.CreateAsync(configureCompany: false);
        (await unconfigured.SendAsync(Meeting(unconfigured, Local(2027, 3, 1, 10), null))).Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
    }

    [Fact]
    public async Task Create_WithoutWebChannel_ShouldFail()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var web = await ctx.Db.CommunicationChannels.SingleAsync();
        web.MarkAsDeleted();
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        var response = await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 10), null));

        response.Message.Should().Be(CalendarErrorCodes.WebChannelNotFound);
    }

    [Fact]
    public async Task Create_InAnotherUsersOrInactiveCalendar_ShouldFail()
    {
        using var ctx = await CalendarTestContext.CreateAsync();

        var foreign = await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 10), null) with { CalendarId = ctx.DefaultCalendar[ctx.OtherId] });
        foreign.Message.Should().Be(CalendarErrorCodes.CalendarNotFound);

        var inactive = new UserCalendar { CompanyId = ctx.CompanyId, OwnerUserId = ctx.OwnerId, Name = "Old", Color = "#000000", IsActive = false };
        ctx.Db.UserCalendars.Add(inactive);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        var response = await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 10), null) with { CalendarId = inactive.Id });
        response.Message.Should().Be(CalendarErrorCodes.CalendarInactive);
    }

    [Fact]
    public async Task Create_WithoutOwnConfiguration_ShouldReturnUserHasNoCalendar()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        ctx.ActingUserId = ctx.StrangerId;

        var response = await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 10), null));

        response.Message.Should().Be(CalendarErrorCodes.UserHasNoCalendar);
    }

    [Theory]
    [InlineData("requesterPersonId")]
    [InlineData("customerId")]
    [InlineData("ticketId")]
    [InlineData("projectId")]
    public async Task Create_WithCrmLinkOfAnotherCompany_ShouldReturnInvalidLink(string field)
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var missing = Guid.NewGuid();
        var command = Meeting(ctx, Local(2027, 3, 1, 10), null);
        command = field switch
        {
            "requesterPersonId" => command with { RequesterPersonId = missing },
            "customerId" => command with { CustomerId = missing },
            "ticketId" => command with { TicketId = missing },
            _ => command with { ProjectId = missing }
        };

        var response = await ctx.SendAsync(command);

        response.Message.Should().Be(CalendarErrorCodes.InvalidLink);
        response.Errors.Should().ContainSingle(e => e.StartsWith(field));
    }

    [Fact]
    public async Task Create_WithValidProjectLink_ShouldKeepIt()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var project = new Project { CompanyId = ctx.CompanyId, Name = "P" };
        ctx.Db.Projects.Add(project);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        var response = await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 10), null) with { ProjectId = project.Id });

        response.Data!.ProjectId.Should().Be(project.Id);
    }

    // ---------------------------------------------------------------- overlap (R4)

    [Fact]
    public async Task Create_OverlappingAPendingMeeting_ShouldReturnOverlapAndTakeTheLock()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var first = await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11)));

        var response = await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 10, 30), Local(2027, 3, 1, 11, 30)));

        response.Message.Should().Be(CalendarErrorCodes.EventOverlap);
        response.Errors.Should().ContainSingle(e => e.Contains(first.ToString()));
        ctx.UnitOfWork.LockRequests.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Create_TouchingEnds_ShouldNotOverlap()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11)));

        (await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 11), Local(2027, 3, 1, 12)))).IsSuccess.Should().BeTrue();
        (await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 9), Local(2027, 3, 1, 10)))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_WhenEitherSideAllowsOverlap_ShouldSucceed()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11), code: CalendarCodes.Activities.Reminder));
        await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11)));

        (await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11), code: CalendarCodes.Activities.Task))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_InAnotherCalendarOfTheSameOwner_ShouldStillOverlap()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var second = new UserCalendar { CompanyId = ctx.CompanyId, OwnerUserId = ctx.OwnerId, Name = "Ventas", Color = "#000000" };
        ctx.Db.UserCalendars.Add(second);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11)));

        var response = await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11)) with { CalendarId = second.Id });

        response.Message.Should().Be(CalendarErrorCodes.EventOverlap);
    }

    [Fact]
    public async Task Create_AgainstAnotherOwner_ShouldNotOverlap()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11)));
        ctx.ActingUserId = ctx.OtherId;

        (await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11)))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_SeriesAgainstALaterOneTimeMeeting_ShouldOverlap()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        await NewEventAsync(ctx, Meeting(ctx, Local(2027, 8, 2, 9), Local(2027, 8, 2, 10)));

        var response = await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 9), Local(2027, 3, 1, 10), "FREQ=WEEKLY;BYDAY=MO"));

        response.Message.Should().Be(CalendarErrorCodes.EventOverlap);
    }

    [Fact]
    public async Task Create_OneTimeAgainstAnOccurrenceOfASeries_ShouldOverlap()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 9), Local(2027, 3, 1, 10), "FREQ=DAILY;COUNT=10"));

        (await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 5, 9, 30), Local(2027, 3, 5, 10, 30)))).Message.Should().Be(CalendarErrorCodes.EventOverlap);
        (await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 12, 9, 30), Local(2027, 3, 12, 10, 30)))).IsSuccess.Should().BeTrue("the series ended on 03-10");
    }

    [Fact]
    public async Task Create_ReturnsAtMostTenConflicts()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 9), Local(2027, 3, 1, 10), "FREQ=DAILY;COUNT=30"));

        var response = await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 9), Local(2027, 3, 1, 10), "FREQ=DAILY;COUNT=30"));

        response.Errors.Should().HaveCount(CalendarEventScheduleGuard.MaxListedConflicts);
    }

    // ---------------------------------------------------------------- recurrence (R7)

    [Theory]
    [InlineData("FREQ=HOURLY")]
    [InlineData("FREQ=DAILY;COUNT=731")]
    [InlineData("FREQ=DAILY;BYSETPOS=1")]
    public async Task Create_WithInvalidRule_ShouldReturnInvalidRecurrenceRule(string rule)
    {
        using var ctx = await CalendarTestContext.CreateAsync();

        var response = await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 9), Local(2027, 3, 1, 10), rule));

        response.Message.Should().Be(CalendarErrorCodes.InvalidRecurrenceRule);
    }

    [Fact]
    public async Task Create_AllDayDailySeriesWithoutEnd_ShouldBeRejected()
    {
        using var ctx = await CalendarTestContext.CreateAsync();

        var response = await ctx.SendAsync(new CreateCalendarEventCommand
        {
            CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.Reminder],
            Title = "x",
            IsAllDay = true,
            StartDate = new DateOnly(2027, 3, 1),
            RecurrenceRule = "FREQ=DAILY"
        });

        response.Message.Should().Be(CalendarErrorCodes.InvalidRecurrenceRule);
    }

    [Fact]
    public async Task Create_Series_ShouldComputeTheEndAndDescribeTheRule()
    {
        using var ctx = await CalendarTestContext.CreateAsync();

        var response = await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 1, 9), Local(2027, 3, 1, 10), "rrule:freq=weekly;byday=MO;count=3"));

        response.Data!.RecurrenceRule.Should().Be("FREQ=WEEKLY;BYDAY=MO;COUNT=3");
        response.Data.RecurrenceEndUtc.Should().Be(Utc(2027, 3, 15, 10));
        response.Data.RecurrenceDescription.Should().Be("Cada semana: lunes; 3 veces");
        response.Data.IsRecurring.Should().BeTrue();
    }

    [Fact]
    public async Task Update_SeriesWithoutScope_ShouldRequireIt()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var id = await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 9), Local(2027, 3, 1, 10), "FREQ=WEEKLY;COUNT=4"));

        var response = await ctx.SendAsync(Update(ctx, id, Local(2027, 3, 1, 9), Local(2027, 3, 1, 10)));

        response.Message.Should().Be(CalendarErrorCodes.RecurrenceScopeRequired);
    }

    [Fact]
    public async Task Update_WithUnknownOccurrence_ShouldReturnOccurrenceNotFound()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var id = await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 9), Local(2027, 3, 1, 10), "FREQ=WEEKLY;COUNT=4"));

        var response = await ctx.SendAsync(Update(ctx, id, Local(2027, 3, 2, 9), Local(2027, 3, 2, 10), CalendarRecurrenceEditScope.ThisOccurrence, Utc(2027, 3, 2, 9)));

        response.Message.Should().Be(CalendarErrorCodes.OccurrenceNotFound);
    }

    [Fact]
    public async Task Update_ThisOccurrence_ShouldCreateAnExceptionAndUpdateItTheSecondTime()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var id = await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 9), Local(2027, 3, 1, 10), "FREQ=WEEKLY;COUNT=4"));
        var key = Utc(2027, 3, 8, 9);

        var first = await ctx.SendAsync(Update(ctx, id, Local(2027, 3, 8, 11), Local(2027, 3, 8, 12), CalendarRecurrenceEditScope.ThisOccurrence, key));
        var second = await ctx.SendAsync(Update(ctx, id, Local(2027, 3, 8, 13), Local(2027, 3, 8, 14), CalendarRecurrenceEditScope.ThisOccurrence, key));

        first.Data!.SeriesMasterId.Should().Be(id);
        first.Data.OriginalStartUtc.Should().Be(key);
        second.Data!.Id.Should().Be(first.Data.Id);
        second.Data.StartUtc.Should().Be(Utc(2027, 3, 8, 13));
        (await ctx.Db.CalendarEvents.IgnoreQueryFilters().CountAsync(e => e.SeriesMasterId == id)).Should().Be(1);
        (await ctx.LogsAsync(first.Data.Id)).Should().OnlyContain(l => l.Action == CalendarEventLogAction.OccurrenceModified);
    }

    [Fact]
    public async Task Update_ExceptionDirectly_ShouldBeTreatedAsSingle()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var id = await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 9), Local(2027, 3, 1, 10), "FREQ=WEEKLY;COUNT=4"));
        var exception = (await ctx.SendAsync(Update(ctx, id, Local(2027, 3, 8, 11), Local(2027, 3, 8, 12), CalendarRecurrenceEditScope.ThisOccurrence, Utc(2027, 3, 8, 9)))).Data!;

        var response = await ctx.SendAsync(Update(ctx, exception.Id, Local(2027, 3, 8, 15), Local(2027, 3, 8, 16), rule: "FREQ=DAILY"));

        response.IsSuccess.Should().BeTrue();
        response.Data!.RecurrenceRule.Should().BeNull("an exception never becomes a series");
    }

    [Fact]
    public async Task Update_ThisAndFollowing_ShouldCutTheSeriesAndMoveLaterExceptions()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var id = await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 9), Local(2027, 3, 1, 10), "FREQ=WEEKLY;BYDAY=MO;COUNT=6"));
        var lateException = (await ctx.SendAsync(Update(ctx, id, Local(2027, 4, 5, 15), Local(2027, 4, 5, 16), CalendarRecurrenceEditScope.ThisOccurrence, Utc(2027, 4, 5, 9)))).Data!;

        var response = await ctx.SendAsync(Update(ctx, id, Local(2027, 3, 22, 10), Local(2027, 3, 22, 11), CalendarRecurrenceEditScope.ThisAndFollowing, Utc(2027, 3, 22, 9)));

        response.IsSuccess.Should().BeTrue(string.Join(" ", response.Errors ?? []));
        var newMaster = response.Data!;
        newMaster.Id.Should().NotBe(id);
        newMaster.RecurrenceRule.Should().Be("FREQ=WEEKLY;BYDAY=MO;COUNT=3");
        newMaster.StartUtc.Should().Be(Utc(2027, 3, 22, 10));

        var original = await ctx.ReloadEventAsync(id);
        original.RecurrenceRule.Should().StartWith("FREQ=WEEKLY;BYDAY=MO;UNTIL=");
        original.RecurrenceEndUtc.Should().Be(Utc(2027, 3, 15, 10));

        var moved = await ctx.ReloadEventAsync(lateException.Id);
        moved.SeriesMasterId.Should().Be(newMaster.Id);
        moved.OriginalStartUtc.Should().Be(Utc(2027, 4, 5, 10), "the key follows the one-hour shift");
        (await ctx.LogsAsync(id)).Should().Contain(l => l.Action == CalendarEventLogAction.SeriesSplit);
        (await ctx.LogsAsync(newMaster.Id)).Should().Contain(l => l.Action == CalendarEventLogAction.SeriesSplit);
    }

    [Fact]
    public async Task Update_ThisAndFollowingOnTheFirstOccurrence_ShouldUpdateTheWholeSeries()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var id = await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 9), Local(2027, 3, 1, 10), "FREQ=WEEKLY;COUNT=3"));

        var response = await ctx.SendAsync(Update(ctx, id, Local(2027, 3, 1, 11), Local(2027, 3, 1, 12), CalendarRecurrenceEditScope.ThisAndFollowing, Utc(2027, 3, 1, 9)));

        response.Data!.Id.Should().Be(id);
        response.Data.StartUtc.Should().Be(Utc(2027, 3, 1, 11));
    }

    [Fact]
    public async Task Update_Series_ShouldShiftTheMasterAndTheExceptionKeys()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var id = await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 9), Local(2027, 3, 1, 10), "FREQ=WEEKLY;COUNT=4"));
        var exception = (await ctx.SendAsync(Update(ctx, id, Local(2027, 3, 8, 15), Local(2027, 3, 8, 16), CalendarRecurrenceEditScope.ThisOccurrence, Utc(2027, 3, 8, 9)))).Data!;

        // Editing the 03-15 occurrence to 10:00 moves the whole series one hour.
        var response = await ctx.SendAsync(Update(ctx, id, Local(2027, 3, 15, 10), Local(2027, 3, 15, 11), CalendarRecurrenceEditScope.Series, Utc(2027, 3, 15, 9)));

        response.IsSuccess.Should().BeTrue(string.Join(" ", response.Errors ?? []));
        response.Data!.StartUtc.Should().Be(Utc(2027, 3, 1, 10));
        (await ctx.ReloadEventAsync(exception.Id)).OriginalStartUtc.Should().Be(Utc(2027, 3, 8, 10));
        (await ctx.LogsAsync(id)).Should().Contain(l => l.Action == CalendarEventLogAction.Rescheduled);
    }

    [Fact]
    public async Task Update_OneTimeIntoSeriesAndMoveCalendar_ShouldLogMovedToCalendar()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var second = new UserCalendar { CompanyId = ctx.CompanyId, OwnerUserId = ctx.OwnerId, Name = "Ventas", Color = "#000000" };
        ctx.Db.UserCalendars.Add(second);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        var id = await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 9), Local(2027, 3, 1, 10)));

        var response = await ctx.SendAsync(Update(ctx, id, Local(2027, 3, 1, 9), Local(2027, 3, 1, 10), rule: "FREQ=DAILY;COUNT=2") with { CalendarId = second.Id });

        response.Data!.CalendarId.Should().Be(second.Id);
        response.Data.RecurrenceRule.Should().Be("FREQ=DAILY;COUNT=2");
        var actions = (await ctx.LogsAsync(id)).Select(l => l.Action).ToList();
        actions.Should().Contain(CalendarEventLogAction.Updated).And.Contain(CalendarEventLogAction.MovedToCalendar);
    }

    // ---------------------------------------------------------------- reschedule status (R6)

    [Fact]
    public async Task PanelUpdate_RescheduledByOthers_ShouldUseTheConfiguredStatus()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        ctx.ActingUserId = ctx.OtherId;
        var id = await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11)));
        ctx.ActingUserId = ctx.OwnerId;

        // A non-RESCHEDULED status configured as reschedule status proves it is read from CalendarCompany.
        var company = await ctx.Db.CalendarCompanies.SingleAsync();
        company.RescheduledByOthersStatusCompanyId = ctx.Status[CalendarCodes.Statuses.Confirmed];
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        var titleOnly = await ctx.SendAsync(new UpdateCalendarPanelEventCommand
        {
            Id = id, CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.Meeting], Title = "Solo título",
            Start = Local(2027, 3, 1, 10), End = Local(2027, 3, 1, 11)
        });
        titleOnly.Data!.Status.Code.Should().Be(CalendarCodes.Statuses.Pending);

        var moved = await ctx.SendAsync(new UpdateCalendarPanelEventCommand
        {
            Id = id, CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.Meeting], Title = "Movida",
            Start = Local(2027, 3, 1, 14), End = Local(2027, 3, 1, 15)
        });
        moved.Data!.Status.Code.Should().Be(CalendarCodes.Statuses.Confirmed);
    }

    [Fact]
    public async Task Update_RescheduledByOwner_ShouldKeepTheStatus()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var id = await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11)));

        var response = await ctx.SendAsync(Update(ctx, id, Local(2027, 3, 1, 14), Local(2027, 3, 1, 15)));

        response.Data!.Status.Code.Should().Be(CalendarCodes.Statuses.Pending);
        (await ctx.LogsAsync(id)).Should().Contain(l => l.Action == CalendarEventLogAction.Rescheduled && l.PreviousStartUtc == Utc(2027, 3, 1, 10));
    }

    [Fact]
    public async Task PanelUpdate_WhenTheRescheduleStatusIsInactive_ShouldFail()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        ctx.ActingUserId = ctx.OtherId;
        var id = await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11)));
        ctx.ActingUserId = ctx.OwnerId;
        var rescheduled = await ctx.Db.CalendarStatusCompanies.SingleAsync(s => s.Id == ctx.Status[CalendarCodes.Statuses.Rescheduled]);
        rescheduled.IsActive = false;
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        var response = await ctx.SendAsync(new UpdateCalendarPanelEventCommand
        {
            Id = id, CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.Meeting], Title = "x",
            Start = Local(2027, 3, 1, 14), End = Local(2027, 3, 1, 15)
        });

        response.Message.Should().Be(CalendarErrorCodes.StatusNotEnabled);
    }

    [Fact]
    public async Task Update_AnotherUsersActivityFromTheOwnCalendar_ShouldBeNotFound()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        ctx.ActingUserId = ctx.OtherId;
        var id = await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11)));
        ctx.ActingUserId = ctx.OwnerId;

        (await ctx.SendAsync(Update(ctx, id, Local(2027, 3, 1, 14), Local(2027, 3, 1, 15)))).Message.Should().Be(CalendarErrorCodes.EventNotFound);
        (await ctx.SendAsync(new DeleteCalendarEventCommand(id))).Message.Should().Be(CalendarErrorCodes.EventNotFound);
    }

    // ---------------------------------------------------------------- business hours (R5)

    [Fact]
    public async Task PanelCreate_OutsideBusinessHours_ShouldFailButTheOwnerMaySchedule()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var panel = new CreateCalendarPanelEventCommand
        {
            OwnerUserId = ctx.OtherId, CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.Meeting], Title = "x",
            Start = Local(2027, 3, 1, 7), End = Local(2027, 3, 1, 8)
        };

        (await ctx.SendAsync(panel)).Message.Should().Be(CalendarErrorCodes.OutsideBusinessHours);

        ctx.ActingUserId = ctx.OtherId;
        // (Without TransactionBehavior the failed attempt above stays saved, so the owner uses another day.)
        (await ctx.SendAsync(Meeting(ctx, Local(2027, 3, 2, 7), Local(2027, 3, 2, 8)))).IsSuccess.Should().BeTrue("R5 does not apply to the owner");
    }

    [Fact]
    public async Task PanelCreate_CrossingMidnight_ShouldFailEvenInA24HourCompany()
    {
        using var ctx = await CalendarTestContext.CreateAsync(open24Hours: true);

        var response = await ctx.SendAsync(new CreateCalendarPanelEventCommand
        {
            OwnerUserId = ctx.OtherId, CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.Meeting], Title = "x",
            Start = Local(2027, 3, 1, 23), End = Local(2027, 3, 2, 1)
        });
        var inside = await ctx.SendAsync(new CreateCalendarPanelEventCommand
        {
            OwnerUserId = ctx.OtherId, CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.Meeting], Title = "x",
            Start = Local(2027, 3, 1, 22), End = Local(2027, 3, 1, 23)
        });

        response.Message.Should().Be(CalendarErrorCodes.OutsideBusinessHours);
        inside.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task PanelCreate_OnAWeekendHolidayOrPeriod_ShouldFail()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        ctx.Db.CalendarCompanyNonWorkingDays.Add(new CalendarCompanyNonWorkingDay { CompanyId = ctx.CompanyId, Date = new DateOnly(2020, 3, 2), Name = "Recurrente", IsRecurringYearly = true });
        ctx.Db.CalendarCompanyNonWorkingDays.Add(new CalendarCompanyNonWorkingDay { CompanyId = ctx.CompanyId, Date = new DateOnly(2027, 3, 3), Name = "Puntual" });
        ctx.Db.CalendarUserNonWorkingPeriods.Add(new CalendarUserNonWorkingPeriod { CompanyId = ctx.CompanyId, UserId = ctx.OtherId, StartDate = new DateOnly(2027, 3, 4), EndDate = new DateOnly(2027, 3, 4), Reason = CalendarNonWorkingReason.Vacation });
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        CreateCalendarPanelEventCommand On(int day) => new()
        {
            OwnerUserId = ctx.OtherId, CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.Meeting], Title = "x",
            Start = Local(2027, 3, day, 10), End = Local(2027, 3, day, 11)
        };

        (await ctx.SendAsync(On(6))).Message.Should().Be(CalendarErrorCodes.OutsideWorkDays);
        (await ctx.SendAsync(On(2))).Message.Should().Be(CalendarErrorCodes.CompanyHoliday);
        (await ctx.SendAsync(On(3))).Message.Should().Be(CalendarErrorCodes.CompanyHoliday);
        var period = await ctx.SendAsync(On(4));
        period.Message.Should().Be(CalendarErrorCodes.UserNotAvailable);
        period.Errors!.Single().Should().NotContain("Vacation", "the reason is never revealed");
        (await ctx.SendAsync(On(5))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task PanelCreate_AllDayOnAWorkDay_ShouldNotCheckHours()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var allDay = new CreateCalendarPanelEventCommand
        {
            OwnerUserId = ctx.OtherId, CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.Task], Title = "x",
            IsAllDay = true, StartDate = new DateOnly(2027, 3, 1)
        };

        (await ctx.SendAsync(allDay)).IsSuccess.Should().BeTrue();
        (await ctx.SendAsync(allDay with { StartDate = new DateOnly(2027, 3, 6) })).Message.Should().Be(CalendarErrorCodes.OutsideWorkDays);
    }

    [Fact]
    public async Task PanelCreate_UsesTheUsersOwnHoursAndRejectsOtherCompanies()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var config = await ctx.Db.CalendarUserConfigurations.SingleAsync(c => c.UserId == ctx.OtherId);
        config.BusinessHoursStart = new TimeOnly(13, 0);
        config.BusinessHoursEnd = new TimeOnly(17, 0);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        var morning = new CreateCalendarPanelEventCommand
        {
            OwnerUserId = ctx.OtherId, CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.Meeting], Title = "x",
            Start = Local(2027, 3, 1, 9), End = Local(2027, 3, 1, 10)
        };

        (await ctx.SendAsync(morning)).Message.Should().Be(CalendarErrorCodes.OutsideBusinessHours);
        (await ctx.SendAsync(morning with { OwnerUserId = ctx.StrangerId })).Message.Should().Be(CalendarErrorCodes.UserNotInCompany);
    }

    [Fact]
    public async Task PanelCreate_ForAMemberWithoutCalendar_ShouldReturnUserHasNoCalendar()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        ctx.Db.UserCompanies.Add(new UserCompany { UserId = ctx.StrangerId, CompanyId = ctx.CompanyId });
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        var response = await ctx.SendAsync(new CreateCalendarPanelEventCommand
        {
            OwnerUserId = ctx.StrangerId, CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.Meeting], Title = "x",
            Start = Local(2027, 3, 1, 9), End = Local(2027, 3, 1, 10)
        });

        response.Message.Should().Be(CalendarErrorCodes.UserHasNoCalendar);
    }

    // ---------------------------------------------------------------- status (R6)

    [Fact]
    public async Task ChangeStatus_CancelRequiresNotesAndReactivatingRevalidates()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var first = await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11)));

        (await ctx.SendAsync(new ChangeCalendarEventStatusCommand { Id = first, CalendarStatusCompanyId = ctx.Status[CalendarCodes.Statuses.Cancelled] }))
            .Message.Should().Be(CalendarErrorCodes.CancellationReasonRequired);

        var cancelled = await ctx.SendAsync(new ChangeCalendarEventStatusCommand { Id = first, CalendarStatusCompanyId = ctx.Status[CalendarCodes.Statuses.Cancelled], Notes = "El cliente canceló" });
        cancelled.Data!.Status.Code.Should().Be(CalendarCodes.Statuses.Cancelled);
        cancelled.Data.StatusChangedByUserId.Should().Be(ctx.OwnerId);
        (await ctx.LogsAsync(first)).Should().Contain(l => l.Action == CalendarEventLogAction.StatusChanged && l.Notes == "El cliente canceló");

        await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11)));

        (await ctx.SendAsync(new ChangeCalendarEventStatusCommand { Id = first, CalendarStatusCompanyId = ctx.Status[CalendarCodes.Statuses.Confirmed] }))
            .Message.Should().Be(CalendarErrorCodes.EventOverlap);
    }

    [Fact]
    public async Task ChangeStatus_WithAnUnusableStatus_ShouldFail()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var id = await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11)));

        (await ctx.SendAsync(new ChangeCalendarEventStatusCommand { Id = id, CalendarStatusCompanyId = Guid.NewGuid() }))
            .Message.Should().Be(CalendarErrorCodes.StatusNotEnabled);
    }

    [Fact]
    public async Task ChangeStatus_ThisOccurrenceAndFollowing_ShouldCreateExceptionAndSplit()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var id = await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 9), Local(2027, 3, 1, 10), "FREQ=WEEKLY;COUNT=6"));

        var single = await ctx.SendAsync(new ChangeCalendarEventStatusCommand
        {
            Id = id, Scope = CalendarRecurrenceEditScope.ThisOccurrence, OccurrenceStartUtc = Utc(2027, 3, 8, 9),
            CalendarStatusCompanyId = ctx.Status[CalendarCodes.Statuses.Cancelled], Notes = "Feriado"
        });
        single.Data!.SeriesMasterId.Should().Be(id);
        single.Data.Status.Code.Should().Be(CalendarCodes.Statuses.Cancelled);

        var following = await ctx.SendAsync(new ChangeCalendarEventStatusCommand
        {
            Id = id, Scope = CalendarRecurrenceEditScope.ThisAndFollowing, OccurrenceStartUtc = Utc(2027, 3, 22, 9),
            CalendarStatusCompanyId = ctx.Status[CalendarCodes.Statuses.Confirmed]
        });
        following.Data!.Id.Should().NotBe(id);
        following.Data.RecurrenceRule.Should().Be("FREQ=WEEKLY;COUNT=3");
        following.Data.Status.Code.Should().Be(CalendarCodes.Statuses.Confirmed);

        var series = await ctx.SendAsync(new ChangeCalendarEventStatusCommand
        {
            Id = id, Scope = CalendarRecurrenceEditScope.Series, OccurrenceStartUtc = Utc(2027, 3, 1, 9),
            CalendarStatusCompanyId = ctx.Status[CalendarCodes.Statuses.Completed]
        });
        series.Data!.Id.Should().Be(id);
        series.Data.Status.IsFinal.Should().BeTrue();
    }

    [Fact]
    public async Task PanelChangeStatus_ShouldRequireCalendarOthersUpdateUnlessSuperAdmin()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        ctx.ActingUserId = ctx.OtherId;
        var id = await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11)));
        ctx.ActingUserId = ctx.OwnerId;
        var command = new ChangeCalendarPanelEventStatusCommand { Id = id, CalendarStatusCompanyId = ctx.Status[CalendarCodes.Statuses.Confirmed] };

        (await ctx.SendAsync(command)).Message.Should().Be(CalendarErrorCodes.OthersPermissionRequired);

        ctx.Permissions.Setup(p => p.HasPermissionAsync(ctx.OwnerId.ToString(), ctx.CompanyId.ToString(), "CalendarOthers", "PATCH", PermissionFlags.CanUpdate))
            .ReturnsAsync(true);
        (await ctx.SendAsync(command)).Data!.Status.Code.Should().Be(CalendarCodes.Statuses.Confirmed);

        ctx.Permissions.Reset();
        ctx.CurrentUser.Setup(c => c.IsInRole("SuperAdmin")).Returns(true);
        (await ctx.SendAsync(command)).IsSuccess.Should().BeTrue();
    }

    // ---------------------------------------------------------------- delete (R8)

    [Fact]
    public async Task Delete_OneTime_ShouldMarkDeletedAndLog()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var id = await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 10), Local(2027, 3, 1, 11)));

        (await ctx.SendAsync(new DeleteCalendarEventCommand(id))).Data.Should().BeTrue();

        (await ctx.ReloadEventAsync(id)).IsDeleted.Should().BeTrue();
        (await ctx.LogsAsync(id)).Should().Contain(l => l.Action == CalendarEventLogAction.Deleted);
        (await ctx.SendAsync(new DeleteCalendarEventCommand(id))).Message.Should().Be(CalendarErrorCodes.EventNotFound);
    }

    [Fact]
    public async Task Delete_SeriesScopes_ShouldCancelOneCutFollowingAndDeleteAll()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var id = await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 9), Local(2027, 3, 1, 10), "FREQ=WEEKLY;COUNT=6"));
        var later = (await ctx.SendAsync(Update(ctx, id, Local(2027, 4, 5, 15), Local(2027, 4, 5, 16), CalendarRecurrenceEditScope.ThisOccurrence, Utc(2027, 4, 5, 9)))).Data!;

        (await ctx.SendAsync(new DeleteCalendarEventCommand(id, CalendarRecurrenceEditScope.ThisOccurrence, Utc(2027, 3, 8, 9)))).IsSuccess.Should().BeTrue();
        var removed = await ctx.Db.CalendarEvents.IgnoreQueryFilters().SingleAsync(e => e.SeriesMasterId == id && e.OriginalStartUtc == Utc(2027, 3, 8, 9));
        removed.CalendarStatusCompanyId.Should().Be(ctx.Status[CalendarCodes.Statuses.Cancelled]);

        (await ctx.SendAsync(new DeleteCalendarEventCommand(id, CalendarRecurrenceEditScope.ThisAndFollowing, Utc(2027, 3, 29, 9)))).IsSuccess.Should().BeTrue();
        (await ctx.ReloadEventAsync(id)).RecurrenceRule.Should().Contain("UNTIL=");
        (await ctx.ReloadEventAsync(later.Id)).IsDeleted.Should().BeTrue();

        (await ctx.SendAsync(new DeleteCalendarPanelEventCommand(id, CalendarRecurrenceEditScope.Series, Utc(2027, 3, 1, 9)))).IsSuccess.Should().BeTrue();
        (await ctx.Db.CalendarEvents.IgnoreQueryFilters().Where(e => e.Id == id || e.SeriesMasterId == id).ToListAsync())
            .Should().OnlyContain(e => e.IsDeleted);
    }

    [Fact]
    public async Task Delete_OccurrenceWithoutCancelledStatus_ShouldFail()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var id = await NewEventAsync(ctx, Meeting(ctx, Local(2027, 3, 1, 9), Local(2027, 3, 1, 10), "FREQ=WEEKLY;COUNT=3"));
        var cancelled = await ctx.Db.CalendarStatusCompanies.SingleAsync(s => s.Id == ctx.Status[CalendarCodes.Statuses.Cancelled]);
        cancelled.MarkAsDeleted();
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        var response = await ctx.SendAsync(new DeleteCalendarEventCommand(id, CalendarRecurrenceEditScope.ThisOccurrence, Utc(2027, 3, 8, 9)));

        response.Message.Should().Be(CalendarErrorCodes.StatusNotEnabled);
    }

    [Fact]
    public async Task PanelCreate_ShouldCreateInTheOwnersDefaultCalendar()
    {
        using var ctx = await CalendarTestContext.CreateAsync();

        var response = await ctx.SendAsync(new CreateCalendarPanelEventCommand
        {
            OwnerUserId = ctx.OtherId, CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.Meeting], Title = "x",
            Start = Local(2027, 3, 1, 9), End = Local(2027, 3, 1, 10)
        });

        response.Data!.OwnerUserId.Should().Be(ctx.OtherId);
        response.Data.CalendarId.Should().Be(ctx.DefaultCalendar[ctx.OtherId]);
        response.Data.Origin.UserId.Should().Be(ctx.OwnerId);
    }
}
