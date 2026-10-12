using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// The supported subset of an RFC 5545 recurrence rule (SPEC 44 R7): <c>FREQ</c> DAILY/WEEKLY/MONTHLY/YEARLY with
/// <c>INTERVAL</c>, <c>BYDAY</c>, <c>BYMONTHDAY</c>, <c>COUNT</c> (at most 730) or <c>UNTIL</c>. Without
/// <c>COUNT</c> and <c>UNTIL</c> the series has no end. Stored without the <c>RRULE:</c> prefix, the same format
/// Google uses (SPEC 46).
/// </summary>
public sealed partial record CalendarRecurrenceRule(
    string Frequency,
    int Interval,
    int? Count,
    DateTime? UntilUtc,
    IReadOnlyList<string> ByDay,
    IReadOnlyList<int> ByMonthDay)
{
    /// <summary> Maximum <c>COUNT</c> of a series. </summary>
    public const int MaxCount = 730;

    private static readonly string[] Frequencies = ["DAILY", "WEEKLY", "MONTHLY", "YEARLY"];
    private static readonly string[] SupportedKeys = ["FREQ", "INTERVAL", "BYDAY", "BYMONTHDAY", "COUNT", "UNTIL"];

    private static readonly Dictionary<string, string> DayNames = new()
    {
        ["MO"] = "lunes", ["TU"] = "martes", ["WE"] = "miércoles", ["TH"] = "jueves",
        ["FR"] = "viernes", ["SA"] = "sábado", ["SU"] = "domingo"
    };

    /// <summary> Whether the series ends (by <c>COUNT</c> or <c>UNTIL</c>). </summary>
    public bool HasEnd => Count.HasValue || UntilUtc.HasValue;

    /// <summary>
    /// Parses and validates a rule. Errors are returned as <c>CALENDAR_INVALID_RECURRENCE_RULE</c>.
    /// </summary>
    public static Response<CalendarRecurrenceRule> Parse(string? rrule)
    {
        if (string.IsNullOrWhiteSpace(rrule))
        {
            return Invalid("The recurrence rule is empty.");
        }

        var text = rrule.Trim();
        if (text.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase))
        {
            text = text[6..];
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2 || pair[1].Length == 0)
            {
                return Invalid($"'{part}' is not a KEY=VALUE pair.");
            }

            var key = pair[0].ToUpperInvariant();
            if (!SupportedKeys.Contains(key))
            {
                return Invalid($"'{key}' is not supported. Allowed: {string.Join(", ", SupportedKeys)}.");
            }

            if (!values.TryAdd(key, pair[1].ToUpperInvariant()))
            {
                return Invalid($"'{key}' appears more than once.");
            }
        }

        if (!values.TryGetValue("FREQ", out var frequency) || !Frequencies.Contains(frequency))
        {
            return Invalid("FREQ is required and must be DAILY, WEEKLY, MONTHLY or YEARLY.");
        }

        var interval = 1;
        if (values.TryGetValue("INTERVAL", out var intervalText)
            && (!int.TryParse(intervalText, NumberStyles.None, CultureInfo.InvariantCulture, out interval) || interval is < 1 or > 999))
        {
            return Invalid("INTERVAL must be an integer between 1 and 999.");
        }

        int? count = null;
        if (values.TryGetValue("COUNT", out var countText))
        {
            if (!int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedCount) || parsedCount is < 1 or > MaxCount)
            {
                return Invalid($"COUNT must be an integer between 1 and {MaxCount}.");
            }

            count = parsedCount;
        }

        DateTime? until = null;
        if (values.TryGetValue("UNTIL", out var untilText))
        {
            if (count.HasValue)
            {
                return Invalid("COUNT and UNTIL cannot be used together.");
            }

            until = ParseUntil(untilText);
            if (until is null)
            {
                return Invalid("UNTIL must be yyyyMMddTHHmmssZ (UTC) or yyyyMMdd.");
            }
        }

        var byDay = new List<string>();
        if (values.TryGetValue("BYDAY", out var byDayText))
        {
            foreach (var day in byDayText.Split(','))
            {
                var match = ByDayRegex().Match(day);
                if (!match.Success
                    || (match.Groups[1].Success && Math.Abs(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)) is 0 or > 53))
                {
                    return Invalid($"BYDAY value '{day}' is not valid.");
                }

                byDay.Add(day);
            }
        }

        var byMonthDay = new List<int>();
        if (values.TryGetValue("BYMONTHDAY", out var byMonthDayText))
        {
            foreach (var day in byMonthDayText.Split(','))
            {
                if (!int.TryParse(day, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedDay) || parsedDay == 0 || Math.Abs(parsedDay) > 31)
                {
                    return Invalid($"BYMONTHDAY value '{day}' is not valid.");
                }

                byMonthDay.Add(parsedDay);
            }
        }

        return new Response<CalendarRecurrenceRule>
        {
            IsSuccess = true,
            Data = new CalendarRecurrenceRule(frequency, interval, count, until, byDay, byMonthDay)
        };
    }

    /// <summary>
    /// The normalized rule text, without the <c>RRULE:</c> prefix.
    /// </summary>
    public string ToRuleString()
    {
        var builder = new StringBuilder("FREQ=").Append(Frequency);
        if (Interval > 1)
        {
            builder.Append(";INTERVAL=").Append(Interval.ToString(CultureInfo.InvariantCulture));
        }

        if (ByDay.Count > 0)
        {
            builder.Append(";BYDAY=").Append(string.Join(',', ByDay));
        }

        if (ByMonthDay.Count > 0)
        {
            builder.Append(";BYMONTHDAY=").Append(string.Join(',', ByMonthDay.Select(d => d.ToString(CultureInfo.InvariantCulture))));
        }

        if (Count.HasValue)
        {
            builder.Append(";COUNT=").Append(Count.Value.ToString(CultureInfo.InvariantCulture));
        }
        else if (UntilUtc.HasValue)
        {
            builder.Append(";UNTIL=").Append(UntilUtc.Value.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary> The same rule ending at <paramref name="untilUtc"/> (drops <c>COUNT</c>). </summary>
    public CalendarRecurrenceRule WithUntil(DateTime untilUtc) => this with { Count = null, UntilUtc = CalendarTime.AsUtc(untilUtc) };

    /// <summary> The same rule limited to <paramref name="count"/> occurrences (drops <c>UNTIL</c>). </summary>
    public CalendarRecurrenceRule WithCount(int count) => this with { Count = count, UntilUtc = null };

    /// <summary>
    /// Human readable description, in Spanish (the frontend language): "Cada 2 semanas: lunes, miércoles; 10 veces".
    /// </summary>
    public string Describe()
    {
        var unit = Frequency switch
        {
            "DAILY" => Interval == 1 ? "Cada día" : $"Cada {Interval} días",
            "WEEKLY" => Interval == 1 ? "Cada semana" : $"Cada {Interval} semanas",
            "MONTHLY" => Interval == 1 ? "Cada mes" : $"Cada {Interval} meses",
            _ => Interval == 1 ? "Cada año" : $"Cada {Interval} años"
        };

        var builder = new StringBuilder(unit);
        if (ByDay.Count > 0)
        {
            builder.Append(": ").Append(string.Join(", ", ByDay.Select(DescribeDay)));
        }

        if (ByMonthDay.Count > 0)
        {
            builder.Append(ByDay.Count > 0 ? "; " : ": ")
                .Append("día ")
                .Append(string.Join(", ", ByMonthDay.Select(d => d > 0 ? d.ToString(CultureInfo.InvariantCulture) : d == -1 ? "último" : $"{-d}° desde el final")));
        }

        if (Count.HasValue)
        {
            builder.Append("; ").Append(Count.Value.ToString(CultureInfo.InvariantCulture)).Append(Count.Value == 1 ? " vez" : " veces");
        }
        else if (UntilUtc.HasValue)
        {
            builder.Append("; hasta el ").Append(UntilUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string DescribeDay(string day)
    {
        var match = ByDayRegex().Match(day);
        var name = DayNames[match.Groups[2].Value];
        if (!match.Groups[1].Success)
        {
            return name;
        }

        var ordinal = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        return ordinal == -1 ? $"último {name}" : ordinal > 0 ? $"{ordinal}° {name}" : $"{-ordinal}° {name} desde el final";
    }

    private static DateTime? ParseUntil(string text)
    {
        if (DateTime.TryParseExact(text, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc))
        {
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        }

        if (DateTime.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return DateTime.SpecifyKind(date.Date.AddDays(1).AddSeconds(-1), DateTimeKind.Utc);
        }

        return null;
    }

    private static Response<CalendarRecurrenceRule> Invalid(string detail) =>
        Response<CalendarRecurrenceRule>.Error(CalendarErrorCodes.InvalidRecurrenceRule, [detail]);

    [GeneratedRegex("^([+-]?\\d{1,2})?(MO|TU|WE|TH|FR|SA|SU)$")]
    private static partial Regex ByDayRegex();
}
