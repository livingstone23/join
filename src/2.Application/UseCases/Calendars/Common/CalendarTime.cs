namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// Time zone helpers of the calendar module (SPEC 44 R3). Every <c>*Utc</c> value handled here has
/// <see cref="DateTimeKind.Utc"/>, because Npgsql rejects any other kind for <c>timestamptz</c>.
/// </summary>
public static class CalendarTime
{
    /// <summary>
    /// Resolves an IANA time zone, or <c>null</c> when it does not exist on this host.
    /// </summary>
    public static TimeZoneInfo? FindTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }

    /// <summary>
    /// Forces <see cref="DateTimeKind.Utc"/> on a value that is already UTC.
    /// </summary>
    public static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    /// <summary>
    /// Converts a UTC instant to the local wall-clock time of the zone.
    /// </summary>
    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), zone), DateTimeKind.Unspecified);

    /// <summary>
    /// Converts a local wall-clock time of the zone to UTC. A time inside a daylight-saving gap moves forward to
    /// the first valid instant.
    /// </summary>
    public static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddMinutes(30);
        }

        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(unspecified, zone), DateTimeKind.Utc);
    }

    /// <summary>
    /// Local midnight of <paramref name="date"/> in the zone, as UTC.
    /// </summary>
    public static DateTime StartOfDayUtc(DateOnly date, TimeZoneInfo zone) =>
        ToUtc(date.ToDateTime(TimeOnly.MinValue), zone);

    /// <summary>
    /// Local date of a UTC instant.
    /// </summary>
    public static DateOnly LocalDate(DateTime utc, TimeZoneInfo zone) => DateOnly.FromDateTime(ToLocal(utc, zone));

    /// <summary>
    /// Enumerates the local dates touched by <c>[startUtc, endUtc)</c> (an all-day activity ending at local
    /// midnight does not touch the next day).
    /// </summary>
    public static IEnumerable<DateOnly> LocalDates(DateTime startUtc, DateTime endUtc, TimeZoneInfo zone)
    {
        var first = LocalDate(startUtc, zone);
        var lastInstant = endUtc > startUtc ? AsUtc(endUtc).AddTicks(-1) : AsUtc(startUtc);
        var last = LocalDate(lastInstant, zone);
        for (var date = first; date <= last; date = date.AddDays(1))
        {
            yield return date;
        }
    }
}
