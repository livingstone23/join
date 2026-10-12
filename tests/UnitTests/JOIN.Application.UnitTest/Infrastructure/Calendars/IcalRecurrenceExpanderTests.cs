using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Infrastructure.Calendars;

namespace JOIN.Application.UnitTest.Infrastructure.Calendars;

/// <summary>
/// Contract tests for <see cref="ICalendarRecurrenceExpander"/>, exercised against the real
/// <see cref="IcalRecurrenceExpander"/> implementation in <c>JOIN.Infrastructure</c>. SPEC 44 lists these
/// tests under "Identified risks — interfaz propia con pruebas de contrato": the interface is the
/// contract, the test suite pins its behaviour so a future swap of the Ical.Net backing stays honest.
/// </summary>
public sealed class IcalRecurrenceExpanderTests
{
    private readonly IcalRecurrenceExpander _sut = new();

    /// <summary> Builds a UTC <see cref="DateTime"/> with <see cref="DateTimeKind.Utc"/>. </summary>
    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    /// <summary> Builds a 1-hour <see cref="CalendarRecurrenceSeries"/> anchored at <paramref name="startUtc"/>. </summary>
    private static CalendarRecurrenceSeries Series(string rule, DateTime startUtc) =>
        new(startUtc, startUtc.AddHours(1), rule, false);

    /// <summary> FREQ=DAILY;INTERVAL=1;COUNT=10 → 10 occurrences inside the window. </summary>
    [Fact]
    public void Expand_WhenDailyIntervalOneWithCountTen_ShouldReturnExactlyTenOccurrences()
    {
        var start = Utc(2027, 1, 1, 14, 0);
        var series = Series("FREQ=DAILY;INTERVAL=1;COUNT=10", start);

        var result = _sut.Expand(series, Utc(2027, 1, 1), Utc(2027, 5, 1), "UTC");

        result.Should().HaveCount(10);
        result.Select(o => o.StartUtc).Should().BeInAscendingOrder();
        result[0].StartUtc.Should().Be(start);
        result[^1].StartUtc.Should().Be(start.AddDays(9));
    }

    /// <summary> FREQ=DAILY;INTERVAL=1 with no COUNT/UNTIL → at least 365 occurrences in a year. </summary>
    [Fact]
    public void Expand_WhenDailyIntervalOneHasNoEnd_ShouldYieldAtLeast365OccurrencesInAYear()
    {
        var start = Utc(2027, 1, 1, 0, 0);
        var series = Series("FREQ=DAILY;INTERVAL=1", start);

        var result = _sut.Expand(series, start, Utc(2028, 1, 1), "UTC");

        result.Should().HaveCountGreaterThanOrEqualTo(365);
    }

    /// <summary> FREQ=WEEKLY;BYDAY=MO,WE;COUNT=10 → 10 occurrences, each on a Monday or Wednesday. </summary>
    [Fact]
    public void Expand_WhenWeeklyByDayMondayWednesdayCountTen_ShouldReturnTenOccurrencesOnlyOnMonWed()
    {
        var start = Utc(2027, 1, 4, 10, 0); // Monday
        var series = Series("FREQ=WEEKLY;BYDAY=MO,WE;COUNT=10", start);

        var result = _sut.Expand(series, start, start.AddDays(91), "UTC");

        result.Should().HaveCount(10);
        result.Should().OnlyContain(o => o.StartUtc.DayOfWeek == DayOfWeek.Monday || o.StartUtc.DayOfWeek == DayOfWeek.Wednesday);
        for (var i = 1; i < result.Count; i++)
        {
            // Alternates 2 days (Mon→Wed) and 5 days (Wed→Mon). Average is 3.5, but each individual gap is 2 or 5.
            var gap = (result[i].StartUtc - result[i - 1].StartUtc).TotalDays;
            (gap == 2 || gap == 5).Should().BeTrue($"expected 2 (Mon→Wed) or 5 (Wed→Mon) days, got {gap}");
        }
    }

    /// <summary> FREQ=WEEKLY;INTERVAL=2;BYDAY=MO → every other Monday. </summary>
    [Fact]
    public void Expand_WhenWeeklyIntervalTwoByDayMonday_ShouldReturnEveryOtherMonday()
    {
        var start = Utc(2027, 1, 4, 10, 0); // Monday
        var series = Series("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO;COUNT=6", start);

        var result = _sut.Expand(series, start, Utc(2027, 4, 5), "UTC");

        result.Should().HaveCount(6);
        result.Should().OnlyContain(o => o.StartUtc.DayOfWeek == DayOfWeek.Monday);
        for (var i = 1; i < result.Count; i++)
        {
            (result[i].StartUtc - result[i - 1].StartUtc).TotalDays.Should().Be(14);
        }
    }

    /// <summary> FREQ=MONTHLY;BYMONTHDAY=15;COUNT=12 → the 15th of each month, 12 occurrences. </summary>
    [Fact]
    public void Expand_WhenMonthlyByMonthDayFifteenCountTwelve_ShouldReturn15thOfEachMonthForAYear()
    {
        var start = Utc(2027, 1, 15, 9, 0);
        var series = Series("FREQ=MONTHLY;BYMONTHDAY=15;COUNT=12", start);

        var result = _sut.Expand(series, start, Utc(2028, 4, 1), "UTC");

        result.Should().HaveCount(12);
        result.Should().OnlyContain(o => o.StartUtc.Day == 15);
        for (var i = 1; i < result.Count; i++)
        {
            result[i].StartUtc.Month.Should().Be(result[i - 1].StartUtc.Month + 1);
        }
        result[^1].StartUtc.Month.Should().Be(12);
        result[^1].StartUtc.Year.Should().Be(2027);
    }

    /// <summary> FREQ=MONTHLY;BYDAY=-1FR;COUNT=6 → the last Friday of each month, 6 occurrences. </summary>
    [Fact]
    public void Expand_WhenMonthlyByDayLastFridayCountSix_ShouldReturnLastFridayOfEachMonth()
    {
        var start = Utc(2027, 1, 29, 9, 0); // Last Friday of January 2027
        var series = Series("FREQ=MONTHLY;BYDAY=-1FR;COUNT=6", start);

        var result = _sut.Expand(series, start, Utc(2027, 8, 1), "UTC");

        result.Should().HaveCount(6);
        result.Should().OnlyContain(o => o.StartUtc.DayOfWeek == DayOfWeek.Friday);
        foreach (var occ in result)
        {
            occ.StartUtc.Day.Should().BeGreaterThanOrEqualTo(25);
            var lastDayOfMonth = DateTime.DaysInMonth(occ.StartUtc.Year, occ.StartUtc.Month);
            var lastFriday = Enumerable.Range(lastDayOfMonth - 6, 7)
                .Select(d => new DateTime(occ.StartUtc.Year, occ.StartUtc.Month, d, 9, 0, 0, DateTimeKind.Utc))
                .First(d => d.DayOfWeek == DayOfWeek.Friday);
            occ.StartUtc.Day.Should().Be(lastFriday.Day);
        }
    }

    /// <summary> FREQ=YEARLY;BYMONTHDAY=1;COUNT=3 → January 1st of three consecutive years. </summary>
    [Fact]
    public void Expand_WhenYearlyByMonthDayOneCountThree_ShouldReturnJanuaryFirstEachYear()
    {
        var start = Utc(2027, 1, 1, 0, 0);
        var series = Series("FREQ=YEARLY;BYMONTHDAY=1;COUNT=3", start);

        var result = _sut.Expand(series, start, Utc(2030, 6, 1), "UTC");

        result.Should().HaveCount(3);
        result.Should().OnlyContain(o => o.StartUtc.Month == 1 && o.StartUtc.Day == 1);
        result[0].StartUtc.Year.Should().Be(2027);
        result[1].StartUtc.Year.Should().Be(2028);
        result[2].StartUtc.Year.Should().Be(2029);
    }

    /// <summary> UTC normalisation: <c>StartUtc.Kind == Utc</c> regardless of the supplied time zone. </summary>
    [Fact]
    public void Expand_WhenTimeZoneIsNonUtc_ShouldReturnOccurrencesInUtcKind()
    {
        var start = Utc(2027, 1, 1, 14, 0);
        var series = Series("FREQ=DAILY;INTERVAL=1;COUNT=3", start);

        var result = _sut.Expand(series, start, Utc(2027, 1, 5), "America/New_York");

        result.Should().HaveCount(3);
        result.Should().OnlyContain(o => o.StartUtc.Kind == DateTimeKind.Utc);
        result.Should().OnlyContain(o => o.EndUtc.Kind == DateTimeKind.Utc);
    }

    /// <summary>
    /// DST handling: DAILY 03:30 America/New_York across the 2027-03-14 spring-forward boundary.
    /// Wall-clock stays at 03:30 local and no occurrence is dropped or duplicated.
    /// </summary>
    [Fact]
    public void Expand_WhenDailyAcrossDstInNewYork_ShouldKeepLocalWallClockAndNotDropOccurrences()
    {
        // Canonical US DST forward: 2027-03-14 02:00 -> 03:00 (second Sunday of March).
        const string rule = "FREQ=DAILY;INTERVAL=1;COUNT=14";
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var startLocal = new DateTime(2027, 3, 7, 3, 30, 0, DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, zone);
        var series = new CalendarRecurrenceSeries(startUtc, startUtc.AddHours(1), rule, false);

        var result = _sut.Expand(series, startUtc, startUtc.AddDays(14), "America/New_York");

        result.Should().HaveCount(14);
        foreach (var occ in result)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(occ.StartUtc, zone);
            local.Hour.Should().Be(3);
            local.Minute.Should().Be(30);
        }

        // UTC offset shifts one hour forward on the day after the spring-forward.
        // Wall-clock 03:30 falls outside the gap (02:00→03:00), so each occurrence stays at
        // the same local 03:30. Before the switch (March 13 03:30 EST) the UTC offset is -5
        // and on/after (March 14 03:30 EDT) it is -4, so the UTC gap between adjacent days is
        // 23 hours (the local 03:30 EDT is one hour earlier in UTC than the previous 03:30 EST).
        var beforeDst = result.Last(o => o.StartUtc.Date < new DateTime(2027, 3, 14, 0, 0, 0, DateTimeKind.Utc));
        var afterDst = result.First(o => o.StartUtc.Date >= new DateTime(2027, 3, 14, 0, 0, 0, DateTimeKind.Utc));
        (afterDst.StartUtc - beforeDst.StartUtc).TotalHours.Should().Be(23);
    }

    /// <summary> FREQ=NONE or missing FREQ → CALENDAR_INVALID_RECURRENCE_RULE. </summary>
    [Theory]
    [InlineData("FREQ=NONE;COUNT=5")]
    [InlineData("COUNT=5")] // missing FREQ
    [InlineData("")]
    public void ValidateRule_WhenFrequencyIsNoneOrMissing_ShouldReturnInvalidRecurrenceRule(string rrule)
    {
        var result = _sut.ValidateRule(rrule);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be(CalendarErrorCodes.InvalidRecurrenceRule);
    }

    /// <summary> FREQ=SECONDLY is outside the supported subset (DAILY/WEEKLY/MONTHLY/YEARLY). </summary>
    [Fact]
    public void ValidateRule_WhenFrequencyIsSecondly_ShouldBeRejected()
    {
        var result = _sut.ValidateRule("FREQ=SECONDLY;COUNT=5");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be(CalendarErrorCodes.InvalidRecurrenceRule);
    }

    /// <summary> COUNT greater than the documented cap (730) → CALENDAR_INVALID_RECURRENCE_RULE. </summary>
    [Fact]
    public void ValidateRule_WhenCountExceedsMax_ShouldBeRejected()
    {
        var result = _sut.ValidateRule($"FREQ=DAILY;COUNT={CalendarRecurrenceRule.MaxCount + 1}");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be(CalendarErrorCodes.InvalidRecurrenceRule);
    }

    /// <summary> Malformed rule (unknown key, bad value, or no KEY=VALUE) → CALENDAR_INVALID_RECURRENCE_RULE. </summary>
    [Theory]
    [InlineData("FOO=BAR")]
    [InlineData("FREQ=DAILY;INTERVAL=abc")]
    [InlineData("not-a-rule")]
    public void ValidateRule_WhenRuleIsMalformed_ShouldBeRejected(string rrule)
    {
        var result = _sut.ValidateRule(rrule);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be(CalendarErrorCodes.InvalidRecurrenceRule);
    }

    /// <summary>
    /// Allowed qualifiers on each supported frequency round-trip through the parser and the Ical.Net
    /// <c>RecurrencePattern</c> construction the implementation uses.
    /// </summary>
    [Theory]
    [InlineData("FREQ=DAILY;COUNT=5")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO,TU;COUNT=10")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=15;COUNT=12")]
    [InlineData("FREQ=YEARLY;BYMONTHDAY=1;COUNT=3")]
    [InlineData("FREQ=DAILY;INTERVAL=2;COUNT=20")]
    public void ValidateRule_WhenRuleIsSupported_ShouldReturnSuccess(string rrule)
    {
        var result = _sut.ValidateRule(rrule);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
    }

    /// <summary> Expand honours UNTIL (no occurrence past the inclusive cap) per RFC 5545. </summary>
    [Fact]
    public void Expand_WhenRuleHasUntil_ShouldNotYieldOccurrencesPastUntil()
    {
        var start = Utc(2027, 1, 1, 12, 0);
        var until = Utc(2027, 1, 10, 12, 0);
        var series = Series($"FREQ=DAILY;UNTIL={until:yyyyMMddTHHmmssZ}", start);

        var result = _sut.Expand(series, start, Utc(2027, 1, 31), "UTC");

        result.Should().NotBeEmpty();
        result.Should().OnlyContain(o => o.StartUtc <= until);
        result[^1].StartUtc.Should().Be(until);
    }

    /// <summary> Expand only returns occurrences whose start falls within the requested half-open window. </summary>
    [Fact]
    public void Expand_ShouldReturnOnlyOccurrencesOverlappingRequestedWindow()
    {
        var start = Utc(2027, 1, 1, 9, 0);
        var series = Series("FREQ=DAILY;INTERVAL=1;COUNT=10", start);
        var windowFrom = Utc(2027, 1, 5);
        var windowTo = Utc(2027, 1, 10);

        var result = _sut.Expand(series, windowFrom, windowTo, "UTC");

        // Capture Jan 5 09:00 .. Jan 9 09:00 — five overlapping 1-hour occurrences (Jan 10 09:00 is excluded).
        result.Should().HaveCount(5);
        result.Should().OnlyContain(o => o.StartUtc >= windowFrom && o.StartUtc < windowTo);
        result.Should().OnlyContain(o => o.StartUtc.Day >= 5 && o.StartUtc.Day < 10);
    }
}
