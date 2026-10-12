using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.Common;

/// <summary>
/// Unit tests for <see cref="CalendarEventScheduleGuard"/> (SPEC 44 R4 and R5).
/// The guard is a pure rules engine over a <see cref="CalendarScheduleCheck"/>; dependencies are mocked so
/// each scenario exercises one rule branch with no DB or library state involved.
/// </summary>
public sealed class CalendarEventScheduleGuardTests
{
    private const string TimeZoneId = "America/Managua";

    private static readonly Fixture _fixture = new();
    private readonly TimeZoneInfo _managua = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    // --------------------------------------------------------------- R4 / overlap

    /// <summary>
    /// R4 — the candidate (10-11) overlaps an existing activity (10:30-11:30). The conflict is listed in
    /// <c>Errors</c> with the existing event id.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_SimpleOverlap_ReturnsEventOverlapWithEventId()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany();
        var existing = CreateScheduleRow(allowsOverlap: false, blocksTime: true);
        context.WithScheduleRows(new[] { existing });

        var check = CreateOverlapCheck(company, ownerId, candidate: Occurrence("2026-01-05T10:00:00Z", "2026-01-05T11:00:00Z"));

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.EventOverlap);
        response.Errors.Should().NotBeNull();
        response.Errors!.Should().HaveCount(1);
        response.Errors!.First().Should().Contain(existing.Id.ToString());
    }

    /// <summary>
    /// R4 — half-open intervals: candidate 10-11 does not overlap with 11-12 (touching end).
    /// </summary>
    [Fact]
    public async Task ValidateAsync_TouchingBoundaries_DoesNotOverlap()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany();
        var existing = CreateScheduleRow(
            startUtc: "2026-01-05T11:00:00Z",
            endUtc: "2026-01-05T12:00:00Z",
            allowsOverlap: false,
            blocksTime: true);
        context.WithScheduleRows(new[] { existing });

        var check = CreateOverlapCheck(company, ownerId, candidate: Occurrence("2026-01-05T10:00:00Z", "2026-01-05T11:00:00Z"));

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// R4 — the candidate's activity type allows overlap; R4 is short-circuited.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_AllowsOverlapOnCandidate_DoesNotRunOverlapCheck()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany();
        var existing = CreateScheduleRow(allowsOverlap: false, blocksTime: true);
        context.WithScheduleRows(new[] { existing });

        var check = CreateOverlapCheck(company, ownerId,
            candidate: Occurrence("2026-01-05T10:00:00Z", "2026-01-05T11:00:00Z"),
            allowsOverlap: true);

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.CalendarEventsMock.Verify(
            x => x.GetScheduleRowsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// R4 — the existing activity's type allows overlap; the row is filtered out before the overlap loop.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_AllowsOverlapOnExisting_DoesNotOverlap()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany();
        var existing = CreateScheduleRow(allowsOverlap: true, blocksTime: true);
        context.WithScheduleRows(new[] { existing });

        var check = CreateOverlapCheck(company, ownerId, candidate: Occurrence("2026-01-05T10:00:00Z", "2026-01-05T11:00:00Z"));

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// R4 — the candidate's status does not block time (e.g. CANCELLED); the overlap check is skipped.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_CancelledCandidateStatus_DoesNotBlockTime()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany();
        var existing = CreateScheduleRow(allowsOverlap: false, blocksTime: true);
        context.WithScheduleRows(new[] { existing });

        var check = CreateOverlapCheck(company, ownerId,
            candidate: Occurrence("2026-01-05T10:00:00Z", "2026-01-05T11:00:00Z"),
            blocksTime: false);

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.CalendarEventsMock.Verify(
            x => x.GetScheduleRowsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// R4 — the candidate's status blocks time (PENDING); the overlap check is run and the conflict is reported.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_PendingStatusBlocksTime_DetectsOverlap()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany();
        var existing = CreateScheduleRow(allowsOverlap: false, blocksTime: true);
        context.WithScheduleRows(new[] { existing });

        var check = CreateOverlapCheck(company, ownerId, candidate: Occurrence("2026-01-05T10:00:00Z", "2026-01-05T11:00:00Z"));

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.EventOverlap);
    }

    /// <summary>
    /// R4 — the schedule query is owner-scoped: a single owner's competing activities across their calendars
    /// are all returned and each overlap is reported.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_SameOwnerAcrossCalendars_ReportsAllOverlaps()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany();
        var firstCalendar = CreateScheduleRow(allowsOverlap: false, blocksTime: true,
            startUtc: "2026-01-05T10:15:00Z", endUtc: "2026-01-05T10:45:00Z");
        var secondCalendar = CreateScheduleRow(allowsOverlap: false, blocksTime: true,
            startUtc: "2026-01-05T10:30:00Z", endUtc: "2026-01-05T11:00:00Z");
        context.WithScheduleRows(new[] { firstCalendar, secondCalendar });

        var check = CreateOverlapCheck(company, ownerId, candidate: Occurrence("2026-01-05T10:00:00Z", "2026-01-05T11:00:00Z"));

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.EventOverlap);
        response.Errors.Should().NotBeNull();
        response.Errors!.Select(e => e).Should().HaveCount(2);
        response.Errors!.First().Should().Contain(firstCalendar.Id.ToString());
        response.Errors!.Skip(1).First().Should().Contain(secondCalendar.Id.ToString());
    }

    /// <summary>
    /// R4 — the existing activity is part of a recurring series. The expander produces an occurrence that
    /// overlaps the candidate; the conflict is reported with the series master id.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_OverlapWithRecurringOccurrence_ReturnsEventOverlap()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany();
        var masterId = _fixture.Create<Guid>();
        var master = new CalendarEventScheduleRow(
            Id: masterId,
            SeriesMasterId: null,
            OriginalStartUtc: null,
            Title: "Daily standup",
            StartUtc: Utc("2026-01-05T10:00:00Z"),
            EndUtc: Utc("2026-01-05T11:00:00Z"),
            IsAllDay: false,
            RecurrenceRule: "FREQ=DAILY",
            RecurrenceEndUtc: null,
            AllowsOverlap: false,
            BlocksTime: true);
        context.WithScheduleRows(new[] { master });
        context.WithExpanderOccurrences(new[]
        {
            Occurrence("2026-01-05T10:00:00Z", "2026-01-05T11:00:00Z")
        });

        var check = CreateOverlapCheck(company, ownerId, candidate: Occurrence("2026-01-05T10:00:00Z", "2026-01-05T11:00:00Z"));

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.EventOverlap);
        response.Errors.Should().NotBeNull();
        response.Errors!.Should().HaveCount(1);
        response.Errors!.First().Should().Contain(masterId.ToString());
    }

    /// <summary>
    /// R4 — the candidate is a new daily series. BuildCandidates expands it across 12 months; one of the
    /// future occurrences collides with an existing single activity, and the conflict is reported.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_NewRecurringSeriesVsExistingSingle_DetectsClashOnFutureOccurrence()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany();
        var existing = CreateScheduleRow(
            startUtc: "2026-06-01T10:30:00Z",
            endUtc: "2026-06-01T11:00:00Z",
            allowsOverlap: false,
            blocksTime: true);
        context.WithScheduleRows(new[] { existing });

        // 6 monthly occurrences; the June one overlaps the existing event.
        var expanded = new List<CalendarOccurrence>
        {
            new(Utc("2026-01-01T10:00:00Z"), Utc("2026-01-01T11:00:00Z")),
            new(Utc("2026-02-01T10:00:00Z"), Utc("2026-02-01T11:00:00Z")),
            new(Utc("2026-03-01T10:00:00Z"), Utc("2026-03-01T11:00:00Z")),
            new(Utc("2026-04-01T10:00:00Z"), Utc("2026-04-01T11:00:00Z")),
            new(Utc("2026-05-01T10:00:00Z"), Utc("2026-05-01T11:00:00Z")),
            new(Utc("2026-06-01T10:00:00Z"), Utc("2026-06-01T11:00:00Z"))
        };
        context.WithExpanderOccurrences(expanded);

        var candidates = guard.BuildCandidates(
            Utc("2026-01-01T10:00:00Z"),
            Utc("2026-01-01T11:00:00Z"),
            "FREQ=DAILY",
            isAllDay: false,
            timeZoneId: "UTC");

        var check = CreateOverlapCheck(company, ownerId, candidate: candidates[0]) with { Candidates = candidates };

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.EventOverlap);
        response.Errors.Should().NotBeNull();
        response.Errors!.Should().HaveCount(1);
        response.Errors!.First().Should().Contain(existing.Id.ToString());
    }

    /// <summary>
    /// R4 — an exception whose status is CANCELLED has effective <c>BlocksTime = false</c>; the row is filtered
    /// out before the overlap loop, releasing the slot.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_CancelledExceptionInSeries_ReleasesTheSlot()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany();
        var cancelledException = CreateScheduleRow(allowsOverlap: false, blocksTime: false);
        context.WithScheduleRows(new[] { cancelledException });

        var check = CreateOverlapCheck(company, ownerId, candidate: Occurrence("2026-01-05T10:00:00Z", "2026-01-05T11:00:00Z"));

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
    }

    // --------------------------------------------------------------- R5 / availability

    /// <summary>
    /// R5 — the activity requires business hours and the actor is NOT the owner. The candidate falls on a
    /// company holiday; <c>CALENDAR_COMPANY_HOLIDAY</c> is reported.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_NonOwnerOnHoliday_ReturnsCompanyHoliday()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany(businessHoursStart: new TimeOnly(9, 0), businessHoursEnd: new TimeOnly(17, 0));
        var userConfig = CreateUserConfiguration(company.CompanyId, ownerId, CalendarWorkDays.Weekdays, new TimeOnly(9, 0), new TimeOnly(17, 0));
        context.WithUserConfigurations(new[] { userConfig });
        var holiday = new CalendarCompanyNonWorkingDay
        {
            CompanyId = company.CompanyId,
            Date = new DateOnly(2026, 1, 12),
            Name = "Founding day",
            IsRecurringYearly = false
        };
        context.WithHolidays(new[] { holiday });

        var check = CreateAvailabilityCheck(company, ownerId, actorIsOwner: false,
            candidate: Occurrence("2026-01-12T15:00:00Z", "2026-01-12T16:00:00Z"));

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyHoliday);
        response.Errors!.Should().ContainMatch("*Founding day*");
    }

    /// <summary>
    /// R5 — when the actor IS the owner, R5 is bypassed even though the candidate would otherwise fall on a
    /// company holiday. R4 is not configured to run; the guard returns success.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_OwnerOnHoliday_SkipsAvailabilityCheck()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany(businessHoursStart: new TimeOnly(9, 0), businessHoursEnd: new TimeOnly(17, 0));
        var userConfig = CreateUserConfiguration(company.CompanyId, ownerId, CalendarWorkDays.Weekdays, new TimeOnly(9, 0), new TimeOnly(17, 0));
        context.WithUserConfigurations(new[] { userConfig });
        var holiday = new CalendarCompanyNonWorkingDay
        {
            CompanyId = company.CompanyId,
            Date = new DateOnly(2026, 1, 12),
            Name = "Founding day",
            IsRecurringYearly = false
        };
        context.WithHolidays(new[] { holiday });

        var check = CreateAvailabilityCheck(company, ownerId, actorIsOwner: true,
            candidate: Occurrence("2026-01-12T15:00:00Z", "2026-01-12T16:00:00Z"));

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// R5 — outside the user's work days, a non-owner gets <c>CALENDAR_OUTSIDE_WORK_DAYS</c>.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_NonOwnerOnNonWorkDay_ReturnsOutsideWorkDays()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany(businessHoursStart: new TimeOnly(9, 0), businessHoursEnd: new TimeOnly(17, 0));
        // 2026-01-10 is a Saturday; Weekdays = Mon-Fri.
        var userConfig = CreateUserConfiguration(company.CompanyId, ownerId, CalendarWorkDays.Weekdays, new TimeOnly(9, 0), new TimeOnly(17, 0));
        context.WithUserConfigurations(new[] { userConfig });

        var check = CreateAvailabilityCheck(company, ownerId, actorIsOwner: false,
            candidate: Occurrence("2026-01-10T15:00:00Z", "2026-01-10T16:00:00Z"));

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.OutsideWorkDays);
    }

    /// <summary>
    /// R5 — the candidate falls inside a non-working period of the owner; the reason is not revealed.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_CandidateInsideNonWorkingPeriod_ReturnsUserNotAvailable()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany(businessHoursStart: new TimeOnly(9, 0), businessHoursEnd: new TimeOnly(17, 0));
        var userConfig = CreateUserConfiguration(company.CompanyId, ownerId, CalendarWorkDays.Weekdays, new TimeOnly(9, 0), new TimeOnly(17, 0));
        context.WithUserConfigurations(new[] { userConfig });
        var period = new CalendarUserNonWorkingPeriod
        {
            CompanyId = company.CompanyId,
            UserId = ownerId,
            StartDate = new DateOnly(2026, 1, 12),
            EndDate = new DateOnly(2026, 1, 12),
            Reason = CalendarNonWorkingReason.Vacation
        };
        context.WithNonWorkingPeriods(new[] { period });

        var check = CreateAvailabilityCheck(company, ownerId, actorIsOwner: false,
            candidate: Occurrence("2026-01-12T15:00:00Z", "2026-01-12T16:00:00Z"));

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.UserNotAvailable);
        response.Errors!.Should().NotContainMatch("*Vacation*");
    }

    /// <summary>
    /// R5 — a single (non-recurring) holiday on the candidate's date blocks the activity.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_SingleHoliday_ReturnsCompanyHoliday()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany(businessHoursStart: new TimeOnly(9, 0), businessHoursEnd: new TimeOnly(17, 0));
        var userConfig = CreateUserConfiguration(company.CompanyId, ownerId, CalendarWorkDays.Weekdays, new TimeOnly(9, 0), new TimeOnly(17, 0));
        context.WithUserConfigurations(new[] { userConfig });
        var holiday = new CalendarCompanyNonWorkingDay
        {
            CompanyId = company.CompanyId,
            Date = new DateOnly(2026, 1, 12),
            Name = "Independence day",
            IsRecurringYearly = false
        };
        context.WithHolidays(new[] { holiday });

        var check = CreateAvailabilityCheck(company, ownerId, actorIsOwner: false,
            candidate: Occurrence("2026-01-12T15:00:00Z", "2026-01-12T16:00:00Z"));

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyHoliday);
    }

    /// <summary>
    /// R5 — a recurring holiday (same month/day, any year) blocks the activity every year.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_RecurringHoliday_ReturnsCompanyHoliday()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany(businessHoursStart: new TimeOnly(9, 0), businessHoursEnd: new TimeOnly(17, 0));
        var userConfig = CreateUserConfiguration(company.CompanyId, ownerId, CalendarWorkDays.Weekdays, new TimeOnly(9, 0), new TimeOnly(17, 0));
        context.WithUserConfigurations(new[] { userConfig });
        var holiday = new CalendarCompanyNonWorkingDay
        {
            CompanyId = company.CompanyId,
            Date = new DateOnly(2020, 1, 12), // 2020-01-12, recurring yearly
            Name = "Founder memorial",
            IsRecurringYearly = true
        };
        context.WithHolidays(new[] { holiday });

        var check = CreateAvailabilityCheck(company, ownerId, actorIsOwner: false,
            candidate: Occurrence("2026-01-12T15:00:00Z", "2026-01-12T16:00:00Z"));

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyHoliday);
        response.Errors!.Should().ContainMatch("*Founder memorial*");
    }

    /// <summary>
    /// R5 — a non-all-day activity outside business hours, by a non-owner, returns
    /// <c>CALENDAR_OUTSIDE_BUSINESS_HOURS</c>.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_NonOwnerOutsideBusinessHours_ReturnsOutsideBusinessHours()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany(businessHoursStart: new TimeOnly(9, 0), businessHoursEnd: new TimeOnly(17, 0));
        var userConfig = CreateUserConfiguration(company.CompanyId, ownerId, CalendarWorkDays.Weekdays, new TimeOnly(9, 0), new TimeOnly(17, 0));
        context.WithUserConfigurations(new[] { userConfig });

        // 2026-01-12 (Monday) 19:00-20:00 local Managua = 01:00-02:00 UTC on 2026-01-13.
        var check = CreateAvailabilityCheck(company, ownerId, actorIsOwner: false,
            candidate: Occurrence("2026-01-13T01:00:00Z", "2026-01-13T02:00:00Z"),
            isAllDay: false);

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.OutsideBusinessHours);
    }

    /// <summary>
    /// R5 — a 24-hour company has effective whole-day business hours; the outside-business-hours check is
    /// bypassed.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_Open24HoursCompany_DoesNotEnforceBusinessHours()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany(isOpen24Hours: true);
        var userConfig = CreateUserConfiguration(company.CompanyId, ownerId, CalendarWorkDays.Weekdays, start: null, end: null);
        context.WithUserConfigurations(new[] { userConfig });

        // 22:00-23:00 local Managua (would be outside 09-17).
        var check = CreateAvailabilityCheck(company, ownerId, actorIsOwner: false,
            candidate: Occurrence("2026-01-13T04:00:00Z", "2026-01-13T05:00:00Z"),
            isAllDay: false);

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// R5 — an all-day activity on a non-work day, by a non-owner, is rejected with
    /// <c>CALENDAR_OUTSIDE_WORK_DAYS</c> (not the business-hours error).
    /// </summary>
    [Fact]
    public async Task ValidateAsync_AllDayOnNonWorkDayByNonOwner_ReturnsOutsideWorkDays()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany(businessHoursStart: new TimeOnly(9, 0), businessHoursEnd: new TimeOnly(17, 0));
        var userConfig = CreateUserConfiguration(company.CompanyId, ownerId, CalendarWorkDays.Weekdays, new TimeOnly(9, 0), new TimeOnly(17, 0));
        context.WithUserConfigurations(new[] { userConfig });

        // 2026-01-10 is a Saturday in Managua. All-day runs from local midnight to next local midnight.
        var check = CreateAvailabilityCheck(company, ownerId, actorIsOwner: false,
            candidate: Occurrence("2026-01-10T06:00:00Z", "2026-01-11T06:00:00Z"),
            isAllDay: true);

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.OutsideWorkDays);
    }

    /// <summary>
    /// R5 — an all-day activity on a work day, by a non-owner, passes (no business-hours check on all-day).
    /// </summary>
    [Fact]
    public async Task ValidateAsync_AllDayOnWorkDayByNonOwner_Succeeds()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany(businessHoursStart: new TimeOnly(9, 0), businessHoursEnd: new TimeOnly(17, 0));
        var userConfig = CreateUserConfiguration(company.CompanyId, ownerId, CalendarWorkDays.Weekdays, new TimeOnly(9, 0), new TimeOnly(17, 0));
        context.WithUserConfigurations(new[] { userConfig });

        // 2026-01-12 is a Monday (work day) in Managua.
        var check = CreateAvailabilityCheck(company, ownerId, actorIsOwner: false,
            candidate: Occurrence("2026-01-12T06:00:00Z", "2026-01-13T06:00:00Z"),
            isAllDay: true);

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// R5 — a non-all-day activity that crosses local midnight falls outside the business-hours window and
    /// returns <c>CALENDAR_OUTSIDE_BUSINESS_HOURS</c>.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_CrossMidnightNonAllDay_ReturnsOutsideBusinessHours()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany(businessHoursStart: new TimeOnly(9, 0), businessHoursEnd: new TimeOnly(17, 0));
        var userConfig = CreateUserConfiguration(company.CompanyId, ownerId, CalendarWorkDays.Weekdays, new TimeOnly(9, 0), new TimeOnly(17, 0));
        context.WithUserConfigurations(new[] { userConfig });

        // 2026-01-09 (Friday) 22:00 local - 2026-01-10 (Saturday) 02:00 local = 04:00-08:00 UTC on 2026-01-10.
        var check = CreateAvailabilityCheck(company, ownerId, actorIsOwner: false,
            candidate: Occurrence("2026-01-10T04:00:00Z", "2026-01-10T08:00:00Z"),
            isAllDay: false);

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.OutsideBusinessHours);
    }

    // --------------------------------------------------------------- horizon / scope

    /// <summary>
    /// SPEC 44, decision 15 — a recurring series is only validated within 12 months. An existing activity
    /// beyond the horizon is not considered; the response is success.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_RecurringSeries_DoesNotValidateBeyondTwelveMonths()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany();
        // The production schedule query is bounded by the candidate range (12 months), so any row beyond
        // the horizon is filtered out at the DB level. The mock returns an empty list for that range.
        context.WithScheduleRows(Array.Empty<CalendarEventScheduleRow>());

        // 12 monthly occurrences, one per month across 2026 — the last one is 2026-12-01, well within
        // the 12-month horizon from the first.
        var expanded = new List<CalendarOccurrence>();
        for (var month = 1; month <= 12; month++)
        {
            var start = new DateTime(2026, month, 1, 10, 0, 0, DateTimeKind.Utc);
            expanded.Add(new CalendarOccurrence(start, start.AddHours(1)));
        }
        context.WithExpanderOccurrences(expanded);

        var candidates = guard.BuildCandidates(
            Utc("2026-01-01T10:00:00Z"),
            Utc("2026-01-01T11:00:00Z"),
            "FREQ=DAILY",
            isAllDay: false,
            timeZoneId: "UTC");

        var check = CreateOverlapCheck(company, ownerId, candidate: candidates[0]) with { Candidates = candidates };

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var scheduleCall = context.CalendarEventsMock.Invocations
            .Single(i => i.Method.Name == nameof(ICalendarEventRepository.GetScheduleRowsAsync));
        var toUtcArg = (DateTime)scheduleCall.Arguments[3];
        toUtcArg.Should().BeOnOrBefore(Utc("2027-01-02T00:00:00Z"));
    }

    /// <summary>
    /// R4 — the schedule query is scoped by <c>OwnerUserId</c>; activities of other users are not returned and
    /// therefore cannot cause a conflict.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_DifferentOwner_DoesNotConsiderTheirEvents()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var otherOwnerId = _fixture.Create<Guid>();
        var company = CreateCompany();

        // The mock returns no rows for the check's owner; in production, the repository filter by OwnerUserId
        // excludes other users' activities.
        context.WithScheduleRowsForOwner(ownerId, Array.Empty<CalendarEventScheduleRow>());

        var check = CreateOverlapCheck(company, ownerId, candidate: Occurrence("2026-01-05T10:00:00Z", "2026-01-05T11:00:00Z"));

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.CalendarEventsMock.Verify(
            x => x.GetScheduleRowsAsync(company.CompanyId, ownerId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        context.CalendarEventsMock.Verify(
            x => x.GetScheduleRowsAsync(company.CompanyId, otherOwnerId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// R4 — the response caps the conflict detail list at <see cref="CalendarEventScheduleGuard.MaxListedConflicts"/>
    /// entries (10) even when more activities overlap the candidate.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_TooManyOverlaps_CapsListedConflictsAtTen()
    {
        var context = new GuardTestContext();
        var guard = context.CreateGuard();
        var ownerId = _fixture.Create<Guid>();
        var company = CreateCompany();
        var rows = Enumerable.Range(0, 12)
            .Select(_ => CreateScheduleRow(
                startUtc: "2026-01-05T10:15:00Z",
                endUtc: "2026-01-05T10:45:00Z",
                allowsOverlap: false,
                blocksTime: true))
            .ToList();
        context.WithScheduleRows(rows);

        var check = CreateOverlapCheck(company, ownerId, candidate: Occurrence("2026-01-05T10:00:00Z", "2026-01-05T11:00:00Z"));

        var response = await guard.ValidateAsync(check, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.EventOverlap);
        response.Errors.Should().NotBeNull();
        response.Errors!.Should().HaveCount(CalendarEventScheduleGuard.MaxListedConflicts);
    }

    // --------------------------------------------------------------- helpers

    private CalendarCompanyContext CreateCompany(
        bool isOpen24Hours = false,
        TimeOnly? businessHoursStart = null,
        TimeOnly? businessHoursEnd = null)
    {
        return new CalendarCompanyContext(
            CompanyId: _fixture.Create<Guid>(),
            CalendarCompanyId: _fixture.Create<Guid>(),
            TimeZoneId: TimeZoneId,
            TimeZone: _managua,
            IsOpen24Hours: isOpen24Hours,
            BusinessHoursStart: businessHoursStart,
            BusinessHoursEnd: businessHoursEnd,
            DefaultActivityUserId: _fixture.Create<Guid>(),
            DefaultCalendarName: "Mi calendario",
            RescheduledByOthersStatusCompanyId: _fixture.Create<Guid>());
    }

    private static CalendarUserConfiguration CreateUserConfiguration(
        Guid companyId,
        Guid userId,
        CalendarWorkDays workDays,
        TimeOnly? start,
        TimeOnly? end)
    {
        return new CalendarUserConfiguration
        {
            CompanyId = companyId,
            UserId = userId,
            WorkDays = workDays,
            BusinessHoursStart = start,
            BusinessHoursEnd = end,
            DefaultCalendarId = Guid.NewGuid()
        };
    }

    private CalendarEventScheduleRow CreateScheduleRow(
        bool allowsOverlap,
        bool blocksTime,
        string startUtc = "2026-01-05T10:30:00Z",
        string endUtc = "2026-01-05T11:30:00Z")
    {
        return new CalendarEventScheduleRow(
            Id: _fixture.Create<Guid>(),
            SeriesMasterId: null,
            OriginalStartUtc: null,
            Title: "Existing",
            StartUtc: Utc(startUtc),
            EndUtc: Utc(endUtc),
            IsAllDay: false,
            RecurrenceRule: null,
            RecurrenceEndUtc: null,
            AllowsOverlap: allowsOverlap,
            BlocksTime: blocksTime);
    }

    private static CalendarOccurrence Occurrence(string startUtc, string endUtc) =>
        new(Utc(startUtc), Utc(endUtc));

    private static DateTime Utc(string value) => DateTime.SpecifyKind(DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);

    private CalendarScheduleCheck CreateOverlapCheck(
        CalendarCompanyContext company,
        Guid ownerId,
        CalendarOccurrence candidate,
        bool allowsOverlap = false,
        bool blocksTime = true) => new(
        Company: company,
        OwnerUserId: ownerId,
        ActorIsOwner: false,
        AllowsOverlap: allowsOverlap,
        RequiresBusinessHours: false,
        BlocksTime: blocksTime,
        IsAllDay: false,
        Candidates: new[] { candidate },
        ExcludedEventIds: new HashSet<Guid>(),
        ExcludedOccurrences: new HashSet<(Guid, DateTime)>());

    private CalendarScheduleCheck CreateAvailabilityCheck(
        CalendarCompanyContext company,
        Guid ownerId,
        bool actorIsOwner,
        CalendarOccurrence candidate,
        bool isAllDay = false) => new(
        Company: company,
        OwnerUserId: ownerId,
        ActorIsOwner: actorIsOwner,
        AllowsOverlap: true,
        RequiresBusinessHours: true,
        BlocksTime: true,
        IsAllDay: isAllDay,
        Candidates: new[] { candidate },
        ExcludedEventIds: new HashSet<Guid>(),
        ExcludedOccurrences: new HashSet<(Guid, DateTime)>());

    /// <summary>
    /// Holds the mocks that the guard reads through <see cref="IUnitOfWork"/> and the recurrence expander.
    /// </summary>
    private sealed class GuardTestContext
    {
        public GuardTestContext()
        {
            UnitOfWorkMock.Setup(x => x.CalendarEvents).Returns(CalendarEventsMock.Object);
            UnitOfWorkMock
                .Setup(x => x.AcquireScheduleLockAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            WithUserConfigurations(Array.Empty<CalendarUserConfiguration>());
            WithNonWorkingPeriods(Array.Empty<CalendarUserNonWorkingPeriod>());
            WithHolidays(Array.Empty<CalendarCompanyNonWorkingDay>());
            WithScheduleRows(Array.Empty<CalendarEventScheduleRow>());
            WithExpanderOccurrences(Array.Empty<CalendarOccurrence>());
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICalendarRecurrenceExpander> ExpanderMock { get; } = new();
        public Mock<ICalendarEventRepository> CalendarEventsMock { get; } = new();

        public void WithUserConfigurations(IEnumerable<CalendarUserConfiguration> configurations)
        {
            var mock = new Mock<IGenericRepository<CalendarUserConfiguration>>();
            mock.SetupRows(configurations);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarUserConfiguration>()).Returns(mock.Object);
        }

        public void WithNonWorkingPeriods(IEnumerable<CalendarUserNonWorkingPeriod> periods)
        {
            var mock = new Mock<IGenericRepository<CalendarUserNonWorkingPeriod>>();
            mock.SetupRows(periods);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarUserNonWorkingPeriod>()).Returns(mock.Object);
        }

        public void WithHolidays(IEnumerable<CalendarCompanyNonWorkingDay> holidays)
        {
            var mock = new Mock<IGenericRepository<CalendarCompanyNonWorkingDay>>();
            mock.SetupRows(holidays);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarCompanyNonWorkingDay>()).Returns(mock.Object);
        }

        public void WithScheduleRows(IEnumerable<CalendarEventScheduleRow> rows)
        {
            CalendarEventsMock
                .Setup(x => x.GetScheduleRowsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(rows.ToList());
        }

        public void WithScheduleRowsForOwner(Guid ownerId, IEnumerable<CalendarEventScheduleRow> rows)
        {
            CalendarEventsMock
                .Setup(x => x.GetScheduleRowsAsync(It.IsAny<Guid>(), ownerId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(rows.ToList());
        }

        public void WithExpanderOccurrences(IEnumerable<CalendarOccurrence> occurrences)
        {
            ExpanderMock
                .Setup(x => x.Expand(It.IsAny<CalendarRecurrenceSeries>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>()))
                .Returns(occurrences.ToList());
        }

        public CalendarEventScheduleGuard CreateGuard() =>
            new(UnitOfWorkMock.Object, ExpanderMock.Object);
    }
}
