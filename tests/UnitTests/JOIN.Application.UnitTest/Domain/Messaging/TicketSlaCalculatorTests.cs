using FluentAssertions;
using JOIN.Domain.Messaging;

namespace JOIN.Application.UnitTest.Domain.Messaging;

/// <summary>
/// Pure unit tests for <see cref="TicketSlaCalculator"/>. No mocks, no DI —
/// the calculator is a static function over its inputs (SPEC 37 F1).
/// </summary>
public sealed class TicketSlaCalculatorTests
{
    private static readonly DateTime CreatedUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int HourCode = 1;
    private const int DayCode = 24;

    [Fact]
    public void Compute_NonFinalTicketBeforeDue_ShouldNotBeBreached()
    {
        var result = TicketSlaCalculator.Compute(
            createdUtc: CreatedUtc,
            resolutionTimeUnits: 3,
            complexityTimeUnitCode: DayCode,
            lastActivityUtc: CreatedUtc.AddHours(1),
            finishedAtUtc: null,
            isFinalStatus: false,
            maxDayTicketInactivity: null,
            nowUtc: CreatedUtc.AddHours(10));

        result.IsSlaBreached.Should().BeFalse();
        result.SlaDueAt.Should().Be(CreatedUtc.AddHours(72));
    }

    [Fact]
    public void Compute_NonFinalTicketAfterDue_ShouldBeBreached()
    {
        var nowUtc = CreatedUtc.AddHours(100);
        var result = TicketSlaCalculator.Compute(
            createdUtc: CreatedUtc,
            resolutionTimeUnits: 3,
            complexityTimeUnitCode: DayCode,
            lastActivityUtc: CreatedUtc.AddHours(5),
            finishedAtUtc: null,
            isFinalStatus: false,
            maxDayTicketInactivity: null,
            nowUtc: nowUtc);

        result.IsSlaBreached.Should().BeTrue();
    }

    [Fact]
    public void Compute_FinalTicketFinishedBeforeDue_ShouldNotBeBreached_EvenLongAfter()
    {
        var finishedAt = CreatedUtc.AddHours(10);
        var result = TicketSlaCalculator.Compute(
            createdUtc: CreatedUtc,
            resolutionTimeUnits: 3,
            complexityTimeUnitCode: DayCode,
            lastActivityUtc: finishedAt,
            finishedAtUtc: finishedAt,
            isFinalStatus: true,
            maxDayTicketInactivity: null,
            nowUtc: CreatedUtc.AddYears(2));

        result.IsSlaBreached.Should().BeFalse();
    }

    [Fact]
    public void Compute_FinalTicketFinishedAfterDue_ShouldBeBreached()
    {
        var finishedAt = CreatedUtc.AddHours(100);
        var result = TicketSlaCalculator.Compute(
            createdUtc: CreatedUtc,
            resolutionTimeUnits: 3,
            complexityTimeUnitCode: DayCode,
            lastActivityUtc: finishedAt,
            finishedAtUtc: finishedAt,
            isFinalStatus: true,
            maxDayTicketInactivity: null,
            nowUtc: finishedAt.AddSeconds(1));

        result.IsSlaBreached.Should().BeTrue();
    }

    [Fact]
    public void Compute_NullInactivityThreshold_ShouldNeverBeInactive()
    {
        var result = TicketSlaCalculator.Compute(
            createdUtc: CreatedUtc,
            resolutionTimeUnits: 1,
            complexityTimeUnitCode: HourCode,
            lastActivityUtc: CreatedUtc,
            finishedAtUtc: null,
            isFinalStatus: false,
            maxDayTicketInactivity: null,
            nowUtc: CreatedUtc.AddYears(5));

        result.IsInactive.Should().BeFalse();
    }

    [Fact]
    public void Compute_ActivityWithinThreshold_ShouldNotBeInactive()
    {
        var result = TicketSlaCalculator.Compute(
            createdUtc: CreatedUtc,
            resolutionTimeUnits: 1,
            complexityTimeUnitCode: HourCode,
            lastActivityUtc: CreatedUtc.AddDays(2),
            finishedAtUtc: null,
            isFinalStatus: false,
            maxDayTicketInactivity: 5,
            nowUtc: CreatedUtc.AddDays(3));

        result.IsInactive.Should().BeFalse();
    }

    [Fact]
    public void Compute_ActivityBeyondThreshold_ShouldBeInactive()
    {
        var result = TicketSlaCalculator.Compute(
            createdUtc: CreatedUtc,
            resolutionTimeUnits: 1,
            complexityTimeUnitCode: HourCode,
            lastActivityUtc: CreatedUtc.AddDays(1),
            finishedAtUtc: null,
            isFinalStatus: false,
            maxDayTicketInactivity: 5,
            nowUtc: CreatedUtc.AddDays(10));

        result.IsInactive.Should().BeTrue();
    }

    [Fact]
    public void Compute_FinalTicketBeyondThreshold_ShouldNotBeInactive()
    {
        var finishedAt = CreatedUtc.AddDays(1);
        var result = TicketSlaCalculator.Compute(
            createdUtc: CreatedUtc,
            resolutionTimeUnits: 1,
            complexityTimeUnitCode: HourCode,
            lastActivityUtc: finishedAt,
            finishedAtUtc: finishedAt,
            isFinalStatus: true,
            maxDayTicketInactivity: 5,
            nowUtc: CreatedUtc.AddYears(1));

        result.IsInactive.Should().BeFalse();
    }

    /// <summary>
    /// Discriminating test: a complexity whose SLA unit is <c>Día</c> (Code = 24) on a
    /// ticket whose own <c>TimeUnitId</c> points to <c>Hora</c> (Code = 1) must
    /// produce <c>SlaDueAt = Created + 72h</c> for <c>resolutionTimeUnits = 3</c>.
    /// If someone accidentally passes the ticket's own unit code (1) instead of the
    /// complexity's, <c>SlaDueAt</c> becomes <c>Created + 3h</c> — this test traps
    /// that exact regression (SPEC 37 risk).
    /// </summary>
    [Fact]
    public void Compute_ResolutionTimeUnitsThree_WithComplexityDayCode_ShouldGiveSeventyTwoHoursNotThree()
    {
        var result = TicketSlaCalculator.Compute(
            createdUtc: CreatedUtc,
            resolutionTimeUnits: 3,
            complexityTimeUnitCode: DayCode,
            lastActivityUtc: CreatedUtc,
            finishedAtUtc: null,
            isFinalStatus: false,
            maxDayTicketInactivity: null,
            nowUtc: CreatedUtc.AddHours(10));

        result.SlaDueAt.Should().Be(CreatedUtc.AddHours(72));
        result.SlaDueAt.Should().NotBe(CreatedUtc.AddHours(3));
    }
}