using Ical.Net;
using Ical.Net.DataTypes;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using IcalEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace JOIN.Infrastructure.Calendars;

/// <summary>
/// <see cref="ICalendarRecurrenceExpander"/> backed by Ical.Net (SPEC 44). The rule is evaluated with a local
/// <c>DTSTART</c> in the company time zone, so daylight saving time keeps the wall-clock start; each occurrence keeps
/// the wall-clock duration of the first one. Every returned value is <see cref="DateTimeKind.Utc"/> (Npgsql rejects
/// any other kind for <c>timestamptz</c>).
/// </summary>
public sealed class IcalRecurrenceExpander : ICalendarRecurrenceExpander
{
    /// <summary> Safety cap when enumerating a bounded series to find its end. </summary>
    private const int MaxEnumeratedOccurrences = 20_000;

    /// <inheritdoc />
    public IReadOnlyList<CalendarOccurrence> Expand(CalendarRecurrenceSeries series, DateTime fromUtc, DateTime toUtc, string timeZoneId)
    {
        var parsed = CalendarRecurrenceRule.Parse(series.RecurrenceRule);
        if (!parsed.IsSuccess || toUtc <= fromUtc)
        {
            return [];
        }

        var context = CreateContext(series, parsed.Data!, timeZoneId);

        // Occurrences that start before the window can still overlap it: start early by the occurrence length.
        var windowStart = CalendarTime.AsUtc(fromUtc) - context.WallDuration - TimeSpan.FromDays(1);
        if (windowStart < context.FirstStartUtc)
        {
            windowStart = context.FirstStartUtc;
        }

        var result = new List<CalendarOccurrence>();
        foreach (var occurrence in context.Event
                     .GetOccurrences(new CalDateTime(windowStart, "UTC", true), null)
                     .TakeWhileBefore(new CalDateTime(CalendarTime.AsUtc(toUtc), "UTC", true)))
        {
            var mapped = Map(occurrence.Period.StartTime, context);
            if (mapped.StartUtc < toUtc && mapped.EndUtc > fromUtc)
            {
                result.Add(mapped);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public Response<CalendarRecurrenceRule> ValidateRule(string rrule)
    {
        var parsed = CalendarRecurrenceRule.Parse(rrule);
        if (!parsed.IsSuccess)
        {
            return parsed;
        }

        try
        {
            _ = new RecurrencePattern(parsed.Data!.ToRuleString());
            return parsed;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return Response<CalendarRecurrenceRule>.Error(CalendarErrorCodes.InvalidRecurrenceRule, [ex.Message]);
        }
    }

    /// <inheritdoc />
    public DateTime? GetSeriesEndUtc(CalendarRecurrenceSeries series, string timeZoneId)
    {
        var parsed = CalendarRecurrenceRule.Parse(series.RecurrenceRule);
        if (!parsed.IsSuccess || !parsed.Data!.HasEnd)
        {
            return null;
        }

        var context = CreateContext(series, parsed.Data, timeZoneId);
        CalendarOccurrence? last = null;
        foreach (var occurrence in context.Event.GetOccurrences(null, null).Take(MaxEnumeratedOccurrences))
        {
            last = Map(occurrence.Period.StartTime, context);
        }

        return last?.EndUtc ?? CalendarTime.AsUtc(series.EndUtc);
    }

    private static ExpansionContext CreateContext(CalendarRecurrenceSeries series, CalendarRecurrenceRule rule, string timeZoneId)
    {
        var zone = CalendarTime.FindTimeZone(timeZoneId) ?? TimeZoneInfo.Utc;
        var zoneId = CalendarTime.FindTimeZone(timeZoneId) is null ? "UTC" : timeZoneId.Trim();

        var localStart = CalendarTime.ToLocal(series.StartUtc, zone);
        var localEnd = CalendarTime.ToLocal(series.EndUtc, zone);

        var icalEvent = new IcalEvent
        {
            DtStart = new CalDateTime(localStart, zoneId, true),
            RecurrenceRule = new RecurrencePattern(rule.ToRuleString())
        };

        return new ExpansionContext(icalEvent, zone, localEnd - localStart, CalendarTime.AsUtc(series.StartUtc));
    }

    private static CalendarOccurrence Map(CalDateTime start, ExpansionContext context)
    {
        // CalDateTime.Value is the wall-clock time in the series zone.
        var localStart = DateTime.SpecifyKind(start.Value, DateTimeKind.Unspecified);
        return new CalendarOccurrence(
            CalendarTime.ToUtc(localStart, context.Zone),
            CalendarTime.ToUtc(localStart + context.WallDuration, context.Zone));
    }

    private sealed record ExpansionContext(IcalEvent Event, TimeZoneInfo Zone, TimeSpan WallDuration, DateTime FirstStartUtc);
}
