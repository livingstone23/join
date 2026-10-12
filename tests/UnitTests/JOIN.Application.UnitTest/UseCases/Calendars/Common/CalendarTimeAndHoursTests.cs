using FluentAssertions;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UnitTest.UseCases.Calendars.Common;

/// <summary>
/// SPEC 44 R3/R5 — time zone helpers, effective business hours and the availability calculator.
/// </summary>
public sealed class CalendarTimeAndHoursTests
{
    private static readonly TimeZoneInfo Managua = TimeZoneInfo.FindSystemTimeZoneById("America/Managua");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Fact]
    public void FindTimeZone_ShouldResolveIanaZonesOnly()
    {
        CalendarTime.FindTimeZone("America/Managua").Should().NotBeNull();
        CalendarTime.FindTimeZone(" America/Managua ").Should().NotBeNull();
        CalendarTime.FindTimeZone("Mars/Olympus").Should().BeNull();
        CalendarTime.FindTimeZone(null).Should().BeNull();
        CalendarTime.FindTimeZone("").Should().BeNull();
    }

    [Fact]
    public void Conversions_ShouldReturnUtcKind()
    {
        var utc = CalendarTime.ToUtc(new DateTime(2027, 3, 1, 10, 0, 0), Managua);
        utc.Should().Be(new DateTime(2027, 3, 1, 16, 0, 0));
        utc.Kind.Should().Be(DateTimeKind.Utc);
        CalendarTime.AsUtc(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)).Kind.Should().Be(DateTimeKind.Utc);
        CalendarTime.ToLocal(utc, Managua).Should().Be(new DateTime(2027, 3, 1, 10, 0, 0));
        CalendarTime.StartOfDayUtc(new DateOnly(2027, 3, 1), Managua).Should().Be(new DateTime(2027, 3, 1, 6, 0, 0));
        CalendarTime.LocalDate(new DateTime(2027, 3, 2, 3, 0, 0, DateTimeKind.Utc), Managua).Should().Be(new DateOnly(2027, 3, 1));
    }

    [Fact]
    public void ToUtc_InsideADaylightSavingGap_ShouldMoveForward()
    {
        // 2027-03-14 02:30 does not exist in New York (clocks jump 02:00 → 03:00).
        CalendarTime.ToUtc(new DateTime(2027, 3, 14, 2, 30, 0), NewYork).Should().Be(new DateTime(2027, 3, 14, 7, 0, 0));
    }

    [Fact]
    public void LocalDates_ShouldNotTouchTheDayAfterAnAllDayEnd()
    {
        var start = new DateTime(2027, 3, 1, 6, 0, 0, DateTimeKind.Utc);

        CalendarTime.LocalDates(start, start.AddDays(2), Managua).Should().Equal(new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 2));
        CalendarTime.LocalDates(start, start, Managua).Should().Equal(new DateOnly(2027, 3, 1));
    }

    [Fact]
    public void BusinessHours_ShouldPreferTheUsersOwnHours()
    {
        var company = new CalendarCompanyContext(Guid.NewGuid(), Guid.NewGuid(), "America/Managua", Managua, false, new TimeOnly(8, 0), new TimeOnly(17, 0), Guid.NewGuid(), "x", Guid.NewGuid());

        CalendarBusinessHours.Effective(company, null).Should().Be(new CalendarBusinessHours(new TimeOnly(8, 0), new TimeOnly(17, 0)));
        CalendarBusinessHours.Effective(company, new CalendarUserConfiguration { BusinessHoursStart = new TimeOnly(9, 0), BusinessHoursEnd = new TimeOnly(12, 0) })
            .ToString().Should().Be("09:00-12:00");
        CalendarBusinessHours.Effective(company with { IsOpen24Hours = true }, null).Should().Be(CalendarBusinessHours.WholeDay);
        CalendarBusinessHours.WholeDay.ToString().Should().Be("00:00-24:00");
    }

    [Fact]
    public void BusinessHours_ContainsWorkDaysAndHolidays()
    {
        var hours = new CalendarBusinessHours(new TimeOnly(8, 0), new TimeOnly(17, 0));
        DateTime L(int day, int hour) => CalendarTime.ToUtc(new DateTime(2027, 3, day, hour, 0, 0), Managua);

        hours.Contains(L(1, 8), L(1, 17), Managua).Should().BeTrue();
        hours.Contains(L(1, 7), L(1, 9), Managua).Should().BeFalse();
        hours.Contains(L(1, 16), L(1, 18), Managua).Should().BeFalse();
        CalendarBusinessHours.WholeDay.Contains(L(1, 23), L(2, 0), Managua).Should().BeTrue("ending at midnight is still the same day");
        CalendarBusinessHours.WholeDay.Contains(L(1, 23), L(2, 1), Managua).Should().BeFalse();

        CalendarBusinessHours.IsWorkDay(CalendarWorkDays.Weekdays, new DateOnly(2027, 3, 1)).Should().BeTrue();
        CalendarBusinessHours.IsWorkDay(CalendarWorkDays.Weekdays, new DateOnly(2027, 3, 7)).Should().BeFalse();
        CalendarBusinessHours.IsHoliday(new CalendarCompanyNonWorkingDay { Date = new DateOnly(2001, 9, 15), IsRecurringYearly = true }, new DateOnly(2027, 9, 15)).Should().BeTrue();
        CalendarBusinessHours.IsHoliday(new CalendarCompanyNonWorkingDay { Date = new DateOnly(2001, 9, 15) }, new DateOnly(2027, 9, 15)).Should().BeFalse();
    }

    [Fact]
    public void Availability_ShouldApplyBusinessRulesOnlyWhenRequired()
    {
        var from = new DateTime(2027, 3, 6, 6, 0, 0, DateTimeKind.Utc); // Saturday 00:00 local
        var to = from.AddDays(2);
        var hours = new CalendarBusinessHours(new TimeOnly(8, 0), new TimeOnly(10, 0));
        var past = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        CalendarAvailabilityCalculator.Compute(from, to, past, Managua, 60, true, false, CalendarWorkDays.Weekdays, hours, [], [], [])
            .Should().BeEmpty("the weekend is not a work day");

        var anyTime = CalendarAvailabilityCalculator.Compute(from, to, past, Managua, 240, false, true, CalendarWorkDays.Weekdays, hours, [], [], []);
        anyTime.Should().HaveCount(12, "six 4-hour blocks per day for two whole days");

        var monday = new DateTime(2027, 3, 8, 6, 0, 0, DateTimeKind.Utc);
        CalendarAvailabilityCalculator.Compute(monday, monday.AddDays(1), past, Managua, 60, true, false, CalendarWorkDays.Weekdays, hours,
            [(new DateOnly(2027, 3, 8), new DateOnly(2027, 3, 8))], [], []).Should().BeEmpty("non-working period");
        CalendarAvailabilityCalculator.Compute(monday, monday.AddDays(1), past, Managua, 60, true, false, CalendarWorkDays.Weekdays, hours,
            [], [new DateOnly(2027, 3, 8)], []).Should().BeEmpty("holiday");
        CalendarAvailabilityCalculator.Compute(monday, monday.AddDays(1), monday.AddDays(2), Managua, 60, true, false, CalendarWorkDays.Weekdays, hours,
            [], [], []).Should().BeEmpty("slots in the past are skipped");
        CalendarAvailabilityCalculator.Compute(monday, monday.AddDays(1), past, Managua, 0, true, false, CalendarWorkDays.Weekdays, hours,
            [], [], []).Should().BeEmpty();
        CalendarAvailabilityCalculator.Compute(monday, monday.AddDays(1), past, Managua, 60, true, true, CalendarWorkDays.Weekdays, hours,
            [], [], [(monday.AddHours(2), monday.AddHours(3))]).Should().Equal(
            new CalendarSlotDto(monday.AddHours(8), monday.AddHours(9)), new CalendarSlotDto(monday.AddHours(9), monday.AddHours(10)));
    }
}
