using System.Globalization;
using System.Text.Json;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// Validates the time of a new or modified activity (SPEC 44 R4 and R5):
/// <list type="bullet">
///   <item>R4 — overlap, for types with <c>AllowsOverlap = false</c> whose status blocks time, for ANY creator.
///   Competing activities: same owner (all their calendars), type without overlap, status with
///   <c>BlocksTime</c>. Touching ends do not overlap. Serialized per owner with a transaction lock.</item>
///   <item>R5 — work days, non-working periods, holidays and business hours, only for types with
///   <c>RequiresBusinessHours</c> when the actor is NOT the owner. The reason of a non-working period is never
///   revealed.</item>
/// </list>
/// </summary>
public sealed class CalendarEventScheduleGuard(IUnitOfWork unitOfWork, ICalendarRecurrenceExpander expander)
{
    /// <summary> Horizon used to validate a series (SPEC 44, decision 15). </summary>
    public static readonly TimeSpan SeriesHorizon = TimeSpan.FromDays(365);

    /// <summary> Maximum number of conflicts listed in a <c>CALENDAR_EVENT_OVERLAP</c> response. </summary>
    public const int MaxListedConflicts = 10;

    private static readonly JsonSerializerOptions ConflictJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Candidate occurrences of an activity: one for a one-time activity; for a series, its occurrences in the next
    /// 12 months (or until its end), minus the ones listed in <paramref name="skippedOccurrenceStarts"/> (keys
    /// replaced by exceptions, validated on their own).
    /// </summary>
    public IReadOnlyList<CalendarOccurrence> BuildCandidates(
        DateTime startUtc,
        DateTime endUtc,
        string? recurrenceRule,
        bool isAllDay,
        string timeZoneId,
        IReadOnlySet<DateTime>? skippedOccurrenceStarts = null)
    {
        if (recurrenceRule is null)
        {
            return [new CalendarOccurrence(CalendarTime.AsUtc(startUtc), CalendarTime.AsUtc(endUtc))];
        }

        var series = new CalendarRecurrenceSeries(startUtc, endUtc, recurrenceRule, isAllDay);
        return expander.Expand(series, startUtc, startUtc + SeriesHorizon, timeZoneId)
            .Where(o => skippedOccurrenceStarts is null || !skippedOccurrenceStarts.Contains(o.StartUtc))
            .ToList();
    }

    /// <summary>
    /// Runs R5 and R4 over the candidate occurrences. Returns an error response with the first rule that fails.
    /// </summary>
    public async Task<Response<bool>> ValidateAsync(CalendarScheduleCheck check, CancellationToken cancellationToken)
    {
        if (check.Candidates.Count == 0)
        {
            return Ok();
        }

        if (check.RequiresBusinessHours && !check.ActorIsOwner)
        {
            var availability = await ValidateAvailabilityAsync(check);
            if (!availability.IsSuccess)
            {
                return availability;
            }
        }

        if (!check.AllowsOverlap && check.BlocksTime)
        {
            return await ValidateOverlapAsync(check, cancellationToken);
        }

        return Ok();
    }

    private async Task<Response<bool>> ValidateAvailabilityAsync(CalendarScheduleCheck check)
    {
        var company = check.Company;
        var configuration = (await unitOfWork.GetRepository<CalendarUserConfiguration>()
                .GetAllIncludingDeletedAsync(x => x.CompanyId == company.CompanyId && x.UserId == check.OwnerUserId && x.GcRecord == 0))
            .FirstOrDefault();

        if (configuration is null)
        {
            return Response<bool>.Error(CalendarErrorCodes.UserHasNoCalendar, ["The owner has no calendar configuration in this company."]);
        }

        var dates = check.Candidates
            .SelectMany(c => check.IsAllDay
                ? CalendarTime.LocalDates(c.StartUtc, c.EndUtc, company.TimeZone)
                : [CalendarTime.LocalDate(c.StartUtc, company.TimeZone)])
            .ToList();
        var minDate = dates.Min();
        var maxDate = dates.Max();

        var periods = await unitOfWork.GetRepository<CalendarUserNonWorkingPeriod>()
            .GetAllIncludingDeletedAsync(x => x.CompanyId == company.CompanyId
                && x.UserId == check.OwnerUserId
                && x.GcRecord == 0
                && x.StartDate <= maxDate
                && x.EndDate >= minDate);

        var holidays = (await unitOfWork.GetRepository<CalendarCompanyNonWorkingDay>()
                .GetAllIncludingDeletedAsync(x => x.CompanyId == company.CompanyId && x.GcRecord == 0))
            .ToList();

        var hours = CalendarBusinessHours.Effective(company, configuration);

        foreach (var candidate in check.Candidates)
        {
            var candidateDates = check.IsAllDay
                ? CalendarTime.LocalDates(candidate.StartUtc, candidate.EndUtc, company.TimeZone).ToList()
                : [CalendarTime.LocalDate(candidate.StartUtc, company.TimeZone)];

            foreach (var date in candidateDates)
            {
                if (!CalendarBusinessHours.IsWorkDay(configuration.WorkDays, date))
                {
                    return Response<bool>.Error(CalendarErrorCodes.OutsideWorkDays,
                        [$"{Format(date)} is not a work day of the user."]);
                }

                if (periods.Any(p => p.StartDate <= date && p.EndDate >= date))
                {
                    return Response<bool>.Error(CalendarErrorCodes.UserNotAvailable,
                        [$"The user is not available on {Format(date)}."]);
                }

                var holiday = holidays.FirstOrDefault(h => CalendarBusinessHours.IsHoliday(h, date));
                if (holiday is not null)
                {
                    return Response<bool>.Error(CalendarErrorCodes.CompanyHoliday,
                        [$"{Format(date)} is a company holiday ({holiday.Name})."]);
                }
            }

            if (!check.IsAllDay && !hours.Contains(candidate.StartUtc, candidate.EndUtc, company.TimeZone))
            {
                return Response<bool>.Error(CalendarErrorCodes.OutsideBusinessHours,
                    [$"Valid business hours: {hours}.", $"The activity must start and end on the same day between {hours} ({company.TimeZoneId})."]);
            }
        }

        return Ok();
    }

    private async Task<Response<bool>> ValidateOverlapAsync(CalendarScheduleCheck check, CancellationToken cancellationToken)
    {
        await unitOfWork.AcquireScheduleLockAsync(check.Company.CompanyId, check.OwnerUserId, cancellationToken);

        var fromUtc = check.Candidates.Min(c => c.StartUtc);
        var toUtc = check.Candidates.Max(c => c.EndUtc);

        var rows = await unitOfWork.CalendarEvents.GetScheduleRowsAsync(
            check.Company.CompanyId, check.OwnerUserId, fromUtc, toUtc, cancellationToken);

        var competing = CalendarOccurrenceExpansion.Expand(rows, fromUtc, toUtc, check.Company.TimeZoneId, expander)
            .Where(o => !o.Source.AllowsOverlap && o.Source.BlocksTime)
            .Where(o => !check.ExcludedEventIds.Contains(o.ExceptionId ?? o.EventId))
            .Where(o => o.ExceptionId.HasValue || !check.ExcludedOccurrences.Contains((o.EventId, o.OccurrenceStartUtc)))
            .ToList();

        var conflicts = new List<string>();
        foreach (var candidate in check.Candidates)
        {
            foreach (var other in competing.Where(o => o.Overlaps(candidate.StartUtc, candidate.EndUtc)))
            {
                var conflict = JsonSerializer.Serialize(new
                {
                    eventId = other.EventId,
                    exceptionId = other.ExceptionId,
                    title = other.Source.Title,
                    startUtc = other.StartUtc,
                    endUtc = other.EndUtc
                }, ConflictJson);

                if (!conflicts.Contains(conflict))
                {
                    conflicts.Add(conflict);
                }

                if (conflicts.Count == MaxListedConflicts)
                {
                    return Overlap(conflicts);
                }
            }
        }

        return conflicts.Count == 0 ? Ok() : Overlap(conflicts);
    }

    private static Response<bool> Overlap(List<string> conflicts) =>
        Response<bool>.Error(CalendarErrorCodes.EventOverlap, conflicts);

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static Response<bool> Ok() => new() { IsSuccess = true, Data = true };
}

/// <summary>
/// Input of <see cref="CalendarEventScheduleGuard.ValidateAsync"/>.
/// </summary>
/// <param name="Company">Calendar context of the company.</param>
/// <param name="OwnerUserId">Owner of the activity (the check is per user, across all their calendars).</param>
/// <param name="ActorIsOwner">Whether the owner makes the change (R5 does not apply then).</param>
/// <param name="AllowsOverlap">Effective <c>AllowsOverlap</c> of the activity type.</param>
/// <param name="RequiresBusinessHours">Effective <c>RequiresBusinessHours</c> of the activity type.</param>
/// <param name="BlocksTime">Whether the activity's status occupies its time (a cancelled one does not).</param>
/// <param name="IsAllDay">All-day activity: no hour check, every covered day must be a work day.</param>
/// <param name="Candidates">Occurrences to validate.</param>
/// <param name="ExcludedEventIds">Rows that must not compete (the activity itself, its master or exceptions).</param>
/// <param name="ExcludedOccurrences">Series occurrences that must not compete (the one an exception replaces).</param>
public sealed record CalendarScheduleCheck(
    CalendarCompanyContext Company,
    Guid OwnerUserId,
    bool ActorIsOwner,
    bool AllowsOverlap,
    bool RequiresBusinessHours,
    bool BlocksTime,
    bool IsAllDay,
    IReadOnlyList<CalendarOccurrence> Candidates,
    IReadOnlySet<Guid> ExcludedEventIds,
    IReadOnlySet<(Guid MasterId, DateTime OccurrenceStartUtc)> ExcludedOccurrences);

/// <summary>
/// Effective business hours of a user (SPEC 44, data model 7): the user's own, otherwise the company's; the whole
/// day when the company is open 24 hours and the user has no hours of their own.
/// </summary>
public sealed record CalendarBusinessHours(TimeOnly Start, TimeOnly? End)
{
    /// <summary> Whole day: <c>00:00</c> to midnight. </summary>
    public static readonly CalendarBusinessHours WholeDay = new(TimeOnly.MinValue, null);

    /// <summary>
    /// The effective hours of <paramref name="configuration"/> in <paramref name="company"/>.
    /// </summary>
    public static CalendarBusinessHours Effective(CalendarCompanyContext company, CalendarUserConfiguration? configuration)
    {
        if (configuration?.BusinessHoursStart is { } userStart && configuration.BusinessHoursEnd is { } userEnd)
        {
            return new CalendarBusinessHours(userStart, userEnd);
        }

        if (company.IsOpen24Hours || company.BusinessHoursStart is null || company.BusinessHoursEnd is null)
        {
            return WholeDay;
        }

        return new CalendarBusinessHours(company.BusinessHoursStart.Value, company.BusinessHoursEnd.Value);
    }

    /// <summary> Whether a day of the week is in the work-day flags. </summary>
    public static bool IsWorkDay(CalendarWorkDays workDays, DateOnly date) =>
        (workDays & (CalendarWorkDays)(1 << (int)date.DayOfWeek)) != 0;

    /// <summary> Whether a holiday applies to a date (recurring holidays match by month and day). </summary>
    public static bool IsHoliday(CalendarCompanyNonWorkingDay holiday, DateOnly date) =>
        holiday.IsRecurringYearly
            ? holiday.Date.Month == date.Month && holiday.Date.Day == date.Day
            : holiday.Date == date;

    /// <summary>
    /// Whether <c>[startUtc, endUtc)</c> starts and ends on the same local day, inside these hours.
    /// </summary>
    public bool Contains(DateTime startUtc, DateTime endUtc, TimeZoneInfo zone)
    {
        var localStart = CalendarTime.ToLocal(startUtc, zone);
        var localEnd = CalendarTime.ToLocal(endUtc, zone);
        var dayStart = localStart.Date;

        var endMinutes = (localEnd - dayStart).TotalMinutes;
        var startMinutes = (localStart - dayStart).TotalMinutes;
        var limitEnd = End is { } end ? end.ToTimeSpan().TotalMinutes : TimeSpan.FromDays(1).TotalMinutes;

        return endMinutes <= TimeSpan.FromDays(1).TotalMinutes
            && startMinutes >= Start.ToTimeSpan().TotalMinutes
            && endMinutes <= limitEnd;
    }

    /// <inheritdoc />
    public override string ToString() =>
        End is { } end
            ? $"{Start.ToString("HH:mm", CultureInfo.InvariantCulture)}-{end.ToString("HH:mm", CultureInfo.InvariantCulture)}"
            : "00:00-24:00";
}
