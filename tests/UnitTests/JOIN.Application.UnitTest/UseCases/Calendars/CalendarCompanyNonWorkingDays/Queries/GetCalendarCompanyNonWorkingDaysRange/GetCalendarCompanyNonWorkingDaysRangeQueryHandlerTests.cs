using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Common;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Queries.GetCalendarCompanyNonWorkingDaysRange;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarCompanyNonWorkingDays.Queries.GetCalendarCompanyNonWorkingDaysRange;

/// <summary>
/// Contains the unit tests for <see cref="GetCalendarCompanyNonWorkingDaysRangeQueryHandler"/>: tenant guard, the
/// range-too-large / inverted-range guard, the empty result, and the DTO mapping of a returned row.
/// </summary>
public sealed class GetCalendarCompanyNonWorkingDaysRangeQueryHandlerTests
{
    private static readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant context is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new GetCalendarCompanyNonWorkingDaysRangeQueryTestContext(Guid.Empty);

        var response = await context.CreateHandler()
            .Handle(new GetCalendarCompanyNonWorkingDaysRangeQuery(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Verifies the inverted-range branch when the upper bound is earlier than the lower bound.
    /// </summary>
    [Fact]
    public async Task Handle_WhenToIsBeforeFrom_ShouldReturnRangeTooLargeError()
    {
        var context = new GetCalendarCompanyNonWorkingDaysRangeQueryTestContext(_fixture.Create<Guid>());

        var response = await context.CreateHandler()
            .Handle(new GetCalendarCompanyNonWorkingDaysRangeQuery(new DateOnly(2026, 6, 30), new DateOnly(2026, 6, 1)), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.RangeTooLarge);
        response.Errors.Should().Contain(s => s.Contains("366"));
    }

    /// <summary>
    /// Verifies the range-too-large branch when the range exceeds 366 days.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRangeExceeds366Days_ShouldReturnRangeTooLargeError()
    {
        var context = new GetCalendarCompanyNonWorkingDaysRangeQueryTestContext(_fixture.Create<Guid>());

        var response = await context.CreateHandler()
            .Handle(new GetCalendarCompanyNonWorkingDaysRangeQuery(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 3)), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.RangeTooLarge);
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path: a single holiday row is mapped to a <see cref="JOIN.Application.DTO.Calendars.CalendarCompanyNonWorkingDayDto"/>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenHolidaysExist_ShouldReturnThem()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new GetCalendarCompanyNonWorkingDaysRangeQueryTestContext(companyId);
        context.Connection.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["Date"] = new DateOnly(2026, 9, 15),
                ["Name"] = "Independence Day",
                ["IsRecurringYearly"] = true,
                ["CreatedAt"] = DateTime.UtcNow
            }));

        var response = await context.CreateHandler()
            .Handle(new GetCalendarCompanyNonWorkingDaysRangeQuery(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Holidays retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Should().HaveCount(1);
        response.Data![0].Name.Should().Be("Independence Day");
        response.Data![0].Date.Should().Be(new DateOnly(2026, 9, 15));
        response.Data![0].IsRecurringYearly.Should().BeTrue();
    }

    /// <summary>
    /// Holds the reusable mocks for the range query tests. The <see cref="CalendarQueryReader"/> is instantiated
    /// directly and the connection factory hands out a <see cref="FakeDbConnection"/> so the Dapper read is
    /// deterministic.
    /// </summary>
    private sealed class GetCalendarCompanyNonWorkingDaysRangeQueryTestContext
    {
        public GetCalendarCompanyNonWorkingDaysRangeQueryTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetCalendarCompanyNonWorkingDaysRangeQueryHandler CreateHandler()
        {
            var reader = new CalendarQueryReader(new NoopExpander());
            return new GetCalendarCompanyNonWorkingDaysRangeQueryHandler(ConnectionFactoryMock.Object, reader, CurrentUserServiceMock.Object);
        }
    }

    /// <summary>
    /// Test double for the recurrence expander: it never produces occurrences, which keeps the range-query test
    /// independent of the Ical.Net dependency.
    /// </summary>
    private sealed class NoopExpander : ICalendarRecurrenceExpander
    {
        public IReadOnlyList<CalendarOccurrence> Expand(CalendarRecurrenceSeries series, DateTime fromUtc, DateTime toUtc, string timeZoneId) =>
            Array.Empty<CalendarOccurrence>();

        public Response<CalendarRecurrenceRule> ValidateRule(string rrule) =>
            CalendarRecurrenceRule.Parse(rrule);

        public DateTime? GetSeriesEndUtc(CalendarRecurrenceSeries series, string timeZoneId) => null;
    }
}
