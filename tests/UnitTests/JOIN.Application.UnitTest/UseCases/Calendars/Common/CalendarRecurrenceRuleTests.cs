using FluentAssertions;
using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UnitTest.UseCases.Calendars.Common;

/// <summary>
/// SPEC 44 R7 — supported RRULE subset, normalization, cut/limit and description.
/// </summary>
public sealed class CalendarRecurrenceRuleTests
{
    [Theory]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE;COUNT=10", "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE;COUNT=10")]
    [InlineData("RRULE:freq=monthly;byday=-1fr", "FREQ=MONTHLY;BYDAY=-1FR")]
    [InlineData("FREQ=YEARLY;BYMONTHDAY=15,-1;UNTIL=20301231T235959Z", "FREQ=YEARLY;BYMONTHDAY=15,-1;UNTIL=20301231T235959Z")]
    [InlineData("FREQ=DAILY;UNTIL=20270310", "FREQ=DAILY;UNTIL=20270310T235959Z")]
    [InlineData(" FREQ=DAILY;INTERVAL=1 ", "FREQ=DAILY")]
    public void Parse_ValidRules_ShouldNormalize(string rule, string expected)
    {
        var parsed = CalendarRecurrenceRule.Parse(rule);

        parsed.IsSuccess.Should().BeTrue(string.Join(" ", parsed.Errors ?? []));
        parsed.Data!.ToRuleString().Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("FREQ")]
    [InlineData("FREQ=")]
    [InlineData("FREQ=HOURLY")]
    [InlineData("INTERVAL=2")]
    [InlineData("FREQ=DAILY;FREQ=WEEKLY")]
    [InlineData("FREQ=DAILY;BYSETPOS=1")]
    [InlineData("FREQ=DAILY;INTERVAL=0")]
    [InlineData("FREQ=DAILY;INTERVAL=x")]
    [InlineData("FREQ=DAILY;COUNT=0")]
    [InlineData("FREQ=DAILY;COUNT=731")]
    [InlineData("FREQ=DAILY;COUNT=2;UNTIL=20300101")]
    [InlineData("FREQ=DAILY;UNTIL=tomorrow")]
    [InlineData("FREQ=WEEKLY;BYDAY=XX")]
    [InlineData("FREQ=MONTHLY;BYDAY=60MO")]
    [InlineData("FREQ=MONTHLY;BYDAY=0MO")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=0")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=32")]
    public void Parse_InvalidRules_ShouldReturnInvalidRecurrenceRule(string? rule)
    {
        var parsed = CalendarRecurrenceRule.Parse(rule);

        parsed.IsSuccess.Should().BeFalse();
        parsed.Message.Should().Be(CalendarErrorCodes.InvalidRecurrenceRule);
        parsed.Errors.Should().ContainSingle();
    }

    [Fact]
    public void WithUntilAndWithCount_ShouldReplaceTheEnd()
    {
        var rule = CalendarRecurrenceRule.Parse("FREQ=WEEKLY;COUNT=5").Data!;

        rule.HasEnd.Should().BeTrue();
        rule.WithUntil(new DateTime(2027, 3, 21, 23, 59, 59)).ToRuleString().Should().Be("FREQ=WEEKLY;UNTIL=20270321T235959Z");
        rule.WithUntil(new DateTime(2027, 3, 21, 23, 59, 59)).WithCount(2).ToRuleString().Should().Be("FREQ=WEEKLY;COUNT=2");
        CalendarRecurrenceRule.Parse("FREQ=DAILY").Data!.HasEnd.Should().BeFalse();
    }

    [Theory]
    [InlineData("FREQ=DAILY", "Cada día")]
    [InlineData("FREQ=DAILY;INTERVAL=3;COUNT=1", "Cada 3 días; 1 vez")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE", "Cada 2 semanas: lunes, miércoles")]
    [InlineData("FREQ=MONTHLY;BYDAY=-1FR", "Cada mes: último viernes")]
    [InlineData("FREQ=MONTHLY;INTERVAL=2;BYDAY=2TU", "Cada 2 meses: 2° martes")]
    [InlineData("FREQ=MONTHLY;BYDAY=-2SU", "Cada mes: 2° domingo desde el final")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=1,-1,-2", "Cada mes: día 1, último, 2° desde el final")]
    [InlineData("FREQ=YEARLY;BYDAY=SA;BYMONTHDAY=13", "Cada año: sábado; día 13")]
    [InlineData("FREQ=YEARLY;INTERVAL=4;UNTIL=20400101T000000Z", "Cada 4 años; hasta el 2040-01-01")]
    [InlineData("FREQ=WEEKLY;BYDAY=TU,TH;COUNT=8", "Cada semana: martes, jueves; 8 veces")]
    public void Describe_ShouldBeReadable(string rule, string expected)
    {
        CalendarRecurrenceRule.Parse(rule).Data!.Describe().Should().Be(expected);
    }
}
