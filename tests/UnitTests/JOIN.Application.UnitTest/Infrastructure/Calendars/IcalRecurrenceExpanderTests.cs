using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Infrastructure.Calendars;

namespace JOIN.Application.UnitTest.Infrastructure.Calendars;

/// <summary>
/// SPEC 44 — contract tests of <see cref="IcalRecurrenceExpander"/> against Ical.Net: daylight saving time, last
/// Friday of the month, COUNT, UNTIL, exceptions keyed by start, and <see cref="DateTimeKind.Utc"/> on every value
/// (Npgsql rejects other kinds for timestamptz).
/// </summary>
public sealed class IcalRecurrenceExpanderTests
{
    private readonly IcalRecurrenceExpander _expander = new();

    private static DateTime Utc(int y, int m, int d, int h) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Expand_ShouldKeepTheWallClockAcrossDaylightSavingTimeAndReturnUtcKind()
    {
        // Sunday 10:00 in New York: 14:00Z before the 2026-11-01 change, 15:00Z after.
        var series = new CalendarRecurrenceSeries(Utc(2026, 10, 25, 14), Utc(2026, 10, 25, 15), "FREQ=WEEKLY;BYDAY=SU;COUNT=3", false);

        var occurrences = _expander.Expand(series, Utc(2026, 10, 1, 0), Utc(2026, 12, 1, 0), "America/New_York");

        occurrences.Select(o => o.StartUtc).Should().Equal(Utc(2026, 10, 25, 14), Utc(2026, 11, 1, 15), Utc(2026, 11, 8, 15));
        occurrences.Should().OnlyContain(o => o.StartUtc.Kind == DateTimeKind.Utc && o.EndUtc.Kind == DateTimeKind.Utc);
        occurrences.Should().OnlyContain(o => o.EndUtc - o.StartUtc == TimeSpan.FromHours(1));
    }

    [Fact]
    public void Expand_LastFridayOfTheMonth_AndOverlappingStartBeforeTheWindow()
    {
        var series = new CalendarRecurrenceSeries(Utc(2027, 1, 29, 15), Utc(2027, 1, 29, 18), "FREQ=MONTHLY;BYDAY=-1FR", false);

        _expander.Expand(series, Utc(2027, 2, 1, 0), Utc(2027, 5, 1, 0), "America/Managua")
            .Select(o => o.StartUtc).Should().Equal(Utc(2027, 2, 26, 15), Utc(2027, 3, 26, 15), Utc(2027, 4, 30, 15));
        _expander.Expand(series, Utc(2027, 2, 26, 16), Utc(2027, 2, 26, 17), "America/Managua")
            .Should().ContainSingle("an occurrence that started before the window still overlaps it");
    }

    [Fact]
    public void Expand_InvalidRuleOrEmptyWindow_ShouldReturnNothing()
    {
        var series = new CalendarRecurrenceSeries(Utc(2027, 1, 1, 15), Utc(2027, 1, 1, 16), "FREQ=HOURLY", false);

        _expander.Expand(series, Utc(2027, 1, 1, 0), Utc(2027, 2, 1, 0), "America/Managua").Should().BeEmpty();
        _expander.Expand(series with { RecurrenceRule = "FREQ=DAILY" }, Utc(2027, 2, 1, 0), Utc(2027, 1, 1, 0), "America/Managua").Should().BeEmpty();
    }

    [Fact]
    public void GetSeriesEndUtc_ShouldUseCountUntilOrNoEnd()
    {
        var start = Utc(2027, 3, 1, 15);
        _expander.GetSeriesEndUtc(new(start, start.AddHours(1), "FREQ=DAILY;COUNT=3", false), "America/Managua").Should().Be(Utc(2027, 3, 3, 16));
        _expander.GetSeriesEndUtc(new(start, start.AddHours(1), "FREQ=WEEKLY;UNTIL=20270315T235959Z", false), "America/Managua").Should().Be(Utc(2027, 3, 15, 16));
        _expander.GetSeriesEndUtc(new(start, start.AddHours(1), "FREQ=DAILY", false), "America/Managua").Should().BeNull();
        _expander.GetSeriesEndUtc(new(start, start.AddHours(1), "FREQ=DAILY;COUNT=2", false), "Unknown/Zone")!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ValidateRule_ShouldUseTheSubsetAndTheLibrary()
    {
        _expander.ValidateRule("FREQ=WEEKLY;BYDAY=MO").IsSuccess.Should().BeTrue();
        _expander.ValidateRule("FREQ=MINUTELY").Message.Should().Be(CalendarErrorCodes.InvalidRecurrenceRule);
    }

    [Fact]
    public void Expansion_ShouldReplaceOccurrencesKeyedByExceptions()
    {
        var master = new Slot(Guid.NewGuid(), null, null, Utc(2027, 3, 1, 15), Utc(2027, 3, 1, 16), false, "FREQ=DAILY;COUNT=3");
        var exception = new Slot(Guid.NewGuid(), master.Id, Utc(2027, 3, 2, 15), Utc(2027, 3, 2, 20), Utc(2027, 3, 2, 21), false, null);

        var occurrences = CalendarOccurrenceExpansion.Expand([master, exception], Utc(2027, 3, 1, 0), Utc(2027, 3, 5, 0), "America/Managua", _expander);

        occurrences.Should().HaveCount(3);
        occurrences[1].ExceptionId.Should().Be(exception.Id);
        occurrences[1].EventId.Should().Be(master.Id);
        occurrences[1].OccurrenceStartUtc.Should().Be(Utc(2027, 3, 2, 15));
        occurrences.Should().OnlyContain(o => o.IsRecurring);
    }

    private sealed record Slot(Guid Id, Guid? SeriesMasterId, DateTime? OriginalStartUtc, DateTime StartUtc, DateTime EndUtc, bool IsAllDay, string? RecurrenceRule)
        : ICalendarEventSlot;
}
