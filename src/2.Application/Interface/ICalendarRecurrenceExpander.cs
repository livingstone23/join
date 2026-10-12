using JOIN.Application.Common;
using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.Interface;

/// <summary>
/// Expands the RFC 5545 recurrence rules of calendar series (SPEC 44 R7). The implementation lives in
/// Infrastructure (Ical.Net) so Application does not depend on the library.
/// </summary>
public interface ICalendarRecurrenceExpander
{
    /// <summary>
    /// Returns the occurrences of <paramref name="series"/> that intersect <c>[fromUtc, toUtc)</c>, ordered by start.
    /// The rule is evaluated in <paramref name="timeZoneId"/> (daylight saving time is respected): every occurrence
    /// keeps the local wall-clock start and duration of the first one. Every returned value is
    /// <see cref="DateTimeKind.Utc"/>.
    /// </summary>
    IReadOnlyList<CalendarOccurrence> Expand(CalendarRecurrenceSeries series, DateTime fromUtc, DateTime toUtc, string timeZoneId);

    /// <summary>
    /// Validates a rule against the supported subset (<c>CALENDAR_INVALID_RECURRENCE_RULE</c>) and the library parser.
    /// </summary>
    Response<CalendarRecurrenceRule> ValidateRule(string rrule);

    /// <summary>
    /// Returns the end of the last occurrence of a series bounded by <c>COUNT</c> or <c>UNTIL</c>, or <c>null</c>
    /// for a series without end.
    /// </summary>
    DateTime? GetSeriesEndUtc(CalendarRecurrenceSeries series, string timeZoneId);
}

/// <summary>
/// A recurring activity to expand: the first occurrence and its rule (without the <c>RRULE:</c> prefix).
/// </summary>
public sealed record CalendarRecurrenceSeries(DateTime StartUtc, DateTime EndUtc, string RecurrenceRule, bool IsAllDay);

/// <summary>
/// One occurrence of a series, in UTC.
/// </summary>
public sealed record CalendarOccurrence(DateTime StartUtc, DateTime EndUtc);
