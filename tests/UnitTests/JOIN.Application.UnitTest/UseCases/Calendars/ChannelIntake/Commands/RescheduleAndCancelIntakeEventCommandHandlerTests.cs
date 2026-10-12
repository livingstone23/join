using FluentAssertions;
using JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;
using JOIN.Application.UseCases.Calendars.ChannelIntake;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.CancelIntakeEvent;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.RescheduleIntakeEvent;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Common;
using Microsoft.EntityFrameworkCore;
using static JOIN.Application.UnitTest.UseCases.Calendars.ChannelIntake.Commands.CreateIntakeEventCommandHandlerTests;
using static JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles.CalendarTestContext;

namespace JOIN.Application.UnitTest.UseCases.Calendars.ChannelIntake.Commands;

/// <summary>
/// SPEC 45 — <c>PUT /events/{id}/reschedule</c> and <c>POST /events/{id}/cancel</c>: only activities of the
/// identified person, never closed ones; reschedule takes the company's reschedule status and is idempotent by
/// message id; cancel takes <c>CANCELLED</c> with the reason; a series changes one occurrence by default.
/// </summary>
public sealed class RescheduleAndCancelIntakeEventCommandHandlerTests
{
    private static RescheduleIntakeEventCommand Reschedule(Guid eventId, Guid personId, int hour = 14, string reference = "wamid.R",
        CalendarRecurrenceEditScope? scope = null, DateTime? occurrence = null, string? notes = "Prefiere la tarde") => new(
        eventId, personId, Local(2027, 3, 1, hour), null, scope, occurrence, "WHATSAPP", "+50588887777", reference, notes);

    private static CancelIntakeEventCommand Cancel(Guid eventId, Guid personId, CalendarRecurrenceEditScope? scope = null, DateTime? occurrence = null) => new(
        eventId, personId, "Ya no puede asistir", scope, occurrence, "WHATSAPP", "+50588887777");

    private static async Task<(CalendarTestContext Ctx, IntakeTestData Data, Guid EventId)> WithIntakeEventAsync(string? rule = null)
    {
        var ctx = await CalendarTestContext.CreateAsync();
        var data = await IntakeTestData.SeedAsync(ctx);
        ctx.Connection.Then(NoDuplicate());
        var created = await ctx.SendAsync(Command(data.PersonId) with { RecurrenceRule = rule });
        created.IsSuccess.Should().BeTrue(created.Message + string.Join(" ", created.Errors ?? []));
        return (ctx, data, created.Data!.EventId);
    }

    [Fact]
    public async Task Reschedule_ShouldMoveTakeTheRescheduleStatusAndLogTheChannel()
    {
        var (ctx, data, eventId) = await WithIntakeEventAsync();
        using var _ = ctx;

        var response = await ctx.SendAsync(Reschedule(eventId, data.PersonId));

        response.IsSuccess.Should().BeTrue(response.Message + string.Join(" ", response.Errors ?? []));
        response.Data!.StartUtc.Should().Be(Utc(2027, 3, 1, 14));
        response.Data.EndUtc.Should().Be(Utc(2027, 3, 1, 15), "the activity keeps its duration");
        response.Data.Status.Code.Should().Be(CalendarCodes.Statuses.Rescheduled);

        var stored = await ctx.ReloadEventAsync(eventId);
        stored.CalendarStatusCompanyId.Should().Be(ctx.Status[CalendarCodes.Statuses.Rescheduled]);
        stored.RequesterPersonId.Should().Be(data.PersonId, "the other fields are kept");
        stored.Summary.Should().Be("Ana quiere revisar la cláusula 4");
        stored.OriginReference.Should().Be("wamid.A");

        var log = (await ctx.LogsAsync(eventId)).Single(l => l.Action == CalendarEventLogAction.Rescheduled);
        log.ChannelId.Should().Be(data.WhatsAppId);
        log.ActorUserId.Should().Be(data.AgentId);
        log.ActorIdentifier.Should().Be("+50588887777");
        log.Notes.Should().Be("[ref:wamid.R] Prefiere la tarde");
        log.PreviousStartUtc.Should().Be(Utc(2027, 3, 1, 10));
    }

    [Fact]
    public async Task Reschedule_WithTheSameReference_ShouldNotApplyAgain()
    {
        var (ctx, data, eventId) = await WithIntakeEventAsync();
        using var _ = ctx;
        (await ctx.SendAsync(Reschedule(eventId, data.PersonId))).IsSuccess.Should().BeTrue();

        var retry = await ctx.SendAsync(Reschedule(eventId, data.PersonId, hour: 15, notes: null));

        retry.IsSuccess.Should().BeTrue();
        retry.Data!.StartUtc.Should().Be(Utc(2027, 3, 1, 14), "the retry answers the state of the first request");
        (await ctx.LogsAsync(eventId)).Count(l => l.Action == CalendarEventLogAction.Rescheduled).Should().Be(1);
    }

    [Fact]
    public async Task Reschedule_IntoAConflict_ShouldFail()
    {
        var (ctx, data, eventId) = await WithIntakeEventAsync();
        using var _ = ctx;
        ctx.Connection.Then(NoDuplicate());
        (await ctx.SendAsync(Command(data.PersonId, hour: 14, reference: "wamid.B"))).IsSuccess.Should().BeTrue();

        (await ctx.SendAsync(Reschedule(eventId, data.PersonId))).Message.Should().Be(CalendarErrorCodes.EventOverlap);
        (await ctx.SendAsync(Reschedule(eventId, data.PersonId, hour: 19, reference: "wamid.C"))).Message.Should().Be(CalendarErrorCodes.OutsideBusinessHours);
    }

    [Fact]
    public async Task OtherPersonOrInternalActivity_ShouldNotBeFound()
    {
        var (ctx, data, eventId) = await WithIntakeEventAsync();
        using var _ = ctx;
        var stored = await ctx.ReloadEventAsync(eventId);
        stored.RequesterPersonId = null;
        ctx.Db.CalendarEvents.Update(stored);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        (await ctx.SendAsync(Reschedule(Guid.NewGuid(), data.PersonId))).Message.Should().Be(CalendarErrorCodes.EventNotFound);
        (await ctx.SendAsync(Reschedule(eventId, data.PersonId))).Message.Should().Be(CalendarErrorCodes.EventNotFound);
        (await ctx.SendAsync(Cancel(eventId, data.PersonId))).Message.Should().Be(CalendarErrorCodes.EventNotFound);
        (await ctx.SendAsync(Cancel(eventId, Guid.Empty))).Message.Should().Be(CalendarErrorCodes.EventNotFound);
    }

    [Fact]
    public async Task ReviewActivity_ShouldNeverBeReachableByTheAgent()
    {
        var (ctx, data, eventId) = await WithIntakeEventAsync();
        using var _ = ctx;
        var stored = await ctx.ReloadEventAsync(eventId);
        stored.CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.PersonReview];
        ctx.Db.CalendarEvents.Update(stored);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        (await ctx.SendAsync(Reschedule(eventId, data.PersonId))).Message.Should().Be(CalendarErrorCodes.EventNotFound);
        (await ctx.SendAsync(Cancel(eventId, data.PersonId))).Message.Should().Be(CalendarErrorCodes.EventNotFound);
    }

    [Fact]
    public async Task Cancel_ShouldTakeCancelledWithTheReasonAndThenBeClosed()
    {
        var (ctx, data, eventId) = await WithIntakeEventAsync();
        using var _ = ctx;

        var response = await ctx.SendAsync(Cancel(eventId, data.PersonId));

        response.IsSuccess.Should().BeTrue(response.Message + string.Join(" ", response.Errors ?? []));
        response.Data!.Status.Code.Should().Be(CalendarCodes.Statuses.Cancelled);
        var log = (await ctx.LogsAsync(eventId)).Single(l => l.Action == CalendarEventLogAction.StatusChanged);
        log.Notes.Should().Be("Ya no puede asistir");
        log.ChannelId.Should().Be(data.WhatsAppId);
        log.ActorIdentifier.Should().Be("+50588887777");

        (await ctx.SendAsync(Cancel(eventId, data.PersonId))).Message.Should().Be(CalendarErrorCodes.IntakeEventAlreadyClosed);
        (await ctx.SendAsync(Reschedule(eventId, data.PersonId))).Message.Should().Be(CalendarErrorCodes.IntakeEventAlreadyClosed);
    }

    [Fact]
    public async Task Series_ShouldChangeOnlyTheOccurrenceByDefault()
    {
        var (ctx, data, eventId) = await WithIntakeEventAsync("FREQ=WEEKLY;COUNT=4");
        using var _ = ctx;
        var second = Utc(2027, 3, 8, 10);

        var moved = await ctx.SendAsync(Reschedule(eventId, data.PersonId, occurrence: second) with { Start = Local(2027, 3, 8, 15) });
        var cancelled = await ctx.SendAsync(Cancel(eventId, data.PersonId, occurrence: Utc(2027, 3, 15, 10)));

        moved.IsSuccess.Should().BeTrue(moved.Message + string.Join(" ", moved.Errors ?? []));
        moved.Data!.EventId.Should().NotBe(eventId, "the occurrence becomes an exception");
        cancelled.IsSuccess.Should().BeTrue(cancelled.Message + string.Join(" ", cancelled.Errors ?? []));
        var exceptions = await ctx.Db.CalendarEvents.IgnoreQueryFilters().Where(e => e.SeriesMasterId == eventId).ToListAsync();
        exceptions.Should().HaveCount(2);
        exceptions.Should().OnlyContain(e => e.OriginReference == null, "an exception never repeats the message id of the master");
        (await ctx.ReloadEventAsync(eventId)).CalendarStatusCompanyId.Should().Be(ctx.Status[CalendarCodes.Statuses.Pending]);
    }

    [Fact]
    public async Task ContextErrors_ShouldFail()
    {
        var (ctx, data, eventId) = await WithIntakeEventAsync();
        using var _ = ctx;

        (await ctx.SendAsync(Reschedule(eventId, data.PersonId) with { OriginChannelCode = CommunicationChannelCodes.Web })).Message.Should().Be(CalendarErrorCodes.IntakeInvalidChannel);
        (await ctx.SendAsync(Cancel(eventId, data.PersonId) with { OriginChannelCode = "SMS" })).Message.Should().Be(CalendarErrorCodes.IntakeInvalidChannel);

        var cancelled = await ctx.Db.CalendarStatusCompanies.SingleAsync(s => s.Id == ctx.Status[CalendarCodes.Statuses.Cancelled]);
        cancelled.GcRecord = 1;
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        (await ctx.SendAsync(Cancel(eventId, data.PersonId))).Message.Should().Be(CalendarErrorCodes.StatusNotEnabled);

        ctx.ActingCompanyId = Guid.NewGuid();
        (await ctx.SendAsync(Reschedule(eventId, data.PersonId))).Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
        (await ctx.SendAsync(Cancel(eventId, data.PersonId))).Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(Reschedule(eventId, data.PersonId))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(Cancel(eventId, data.PersonId))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    [Fact]
    public void MovedFrom_AllDay_ShouldKeepTheNumberOfDaysInTheCompanyZone()
    {
        var current = new CalendarEvent
        {
            IsAllDay = true,
            Title = "Inventario",
            StartUtc = new DateTime(2027, 3, 1, 6, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2027, 3, 3, 6, 0, 0, DateTimeKind.Utc),
            RequesterPersonId = Guid.NewGuid()
        };
        var zone = CalendarTime.FindTimeZone("America/Managua")!;

        var input = IntakeEventInput.MovedFrom(current, Local(2027, 3, 10, 23), Local(2027, 3, 20, 0), zone);

        input.Start.Should().BeNull();
        input.StartDate.Should().Be(new DateOnly(2027, 3, 10));
        input.EndDate.Should().Be(new DateOnly(2027, 3, 11));
        input.RequesterPersonId.Should().Be(current.RequesterPersonId);
        input.Title.Should().Be("Inventario");
    }

    [Fact]
    public void DefaultScope_ShouldBeThisOccurrenceOnlyForASeriesMaster()
    {
        var master = new CalendarEvent { RecurrenceRule = "FREQ=DAILY" };
        var exception = new CalendarEvent { SeriesMasterId = Guid.NewGuid() };

        IntakeEventGuard.DefaultScope(master, null).Should().Be(CalendarRecurrenceEditScope.ThisOccurrence);
        IntakeEventGuard.DefaultScope(master, CalendarRecurrenceEditScope.Series).Should().Be(CalendarRecurrenceEditScope.Series);
        IntakeEventGuard.DefaultScope(exception, null).Should().BeNull();
    }
}
