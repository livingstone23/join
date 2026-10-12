using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Queries.GetUpcomingCalendarEvents;
using JOIN.Application.UseCases.Calendars.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEvents.Queries.GetUpcomingCalendarEvents;

/// <summary>
/// SPEC 44 F10 / CalendarEvents group — upcoming query. <c>days</c> must be in [1..31] (the docstring says
/// "1..31"; 0 and 32 are rejected), default is 7, and the range starts at <c>UtcNow</c>.
/// </summary>
public sealed class GetUpcomingCalendarEventsQueryHandlerTests
{
    private static readonly Fixture _fixture = new();
    private const string TimeZoneId = "America/Managua";

    /// <summary>
    /// <c>days=0</c> is rejected with <c>CALENDAR_RANGE_TOO_LARGE</c>; the handler never touches the database.
    /// </summary>
    [Fact]
    public async Task Handle_WhenDaysIsZero_ShouldReturnRangeTooLarge()
    {
        var context = new GetUpcomingTestContext();

        var response = await context.CreateHandler().Handle(new GetUpcomingCalendarEventsQuery(0), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.RangeTooLarge);
    }

    /// <summary>
    /// <c>days=32</c> is rejected with the same code.
    /// </summary>
    [Fact]
    public async Task Handle_WhenDaysIsThirtyTwo_ShouldReturnRangeTooLarge()
    {
        var context = new GetUpcomingTestContext();

        var response = await context.CreateHandler().Handle(new GetUpcomingCalendarEventsQuery(32), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.RangeTooLarge);
    }

    /// <summary>
    /// Default <c>days</c> (7) sends a range of [now, now + 7d]; the parameter
    /// <c>@To</c> is <c>now + 7 days</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenDaysDefault_ShouldSpanSevenDays()
    {
        var context = new GetUpcomingTestContext();
        context.ConnectionFactory.SetResults(
            context.CalendarCompanyRow(),
            FakeResultSet.Empty("Id", "SeriesMasterId", "OriginalStartUtc", "CalendarId", "CalendarName",
                "CalendarColor", "OwnerUserId", "OwnerName", "Title", "Summary", "Location", "VideoCallUrl",
                "IsAllDay", "StartUtc", "EndUtc", "RecurrenceRule", "RecurrenceEndUtc",
                "ActivityCompanyId", "ActivityCode", "ActivityName", "ActivityColor", "AllowsOverlap",
                "StatusCompanyId", "StatusCode", "StatusName", "StatusColor", "BlocksTime", "IsFinal",
                "ChannelCode", "OriginType", "OriginIdentifier", "OriginUserId"),
            FakeResultSet.Empty("Id", "Date", "Name", "IsRecurringYearly", "CreatedAt"),
            FakeResultSet.Empty("Id", "UserId", "StartDate", "EndDate", "Reason", "Notes", "CreatedAt"));

        var before = DateTime.UtcNow;
        var response = await context.CreateHandler().Handle(new GetUpcomingCalendarEventsQuery(), CancellationToken.None);
        var after = DateTime.UtcNow;

        response.IsSuccess.Should().BeTrue();
        var fromParam = (DateTime)context.ConnectionFactory.CapturedParameters["From"]!;
        var toParam = (DateTime)context.ConnectionFactory.CapturedParameters["To"]!;
        fromParam.Should().BeOnOrAfter(before.AddSeconds(-1)).And.BeOnOrBefore(after.AddSeconds(1));
        (toParam - fromParam).TotalDays.Should().BeApproximately(7, 0.01);
    }

    /// <summary>
    /// <c>days=1</c> sends a one-day range.
    /// </summary>
    [Fact]
    public async Task Handle_WhenDaysIsOne_ShouldSpanOneDay()
    {
        var context = new GetUpcomingTestContext();
        context.ConnectionFactory.SetResults(
            context.CalendarCompanyRow(),
            FakeResultSet.Empty("Id", "SeriesMasterId", "OriginalStartUtc", "CalendarId", "CalendarName",
                "CalendarColor", "OwnerUserId", "OwnerName", "Title", "Summary", "Location", "VideoCallUrl",
                "IsAllDay", "StartUtc", "EndUtc", "RecurrenceRule", "RecurrenceEndUtc",
                "ActivityCompanyId", "ActivityCode", "ActivityName", "ActivityColor", "AllowsOverlap",
                "StatusCompanyId", "StatusCode", "StatusName", "StatusColor", "BlocksTime", "IsFinal",
                "ChannelCode", "OriginType", "OriginIdentifier", "OriginUserId"),
            FakeResultSet.Empty("Id", "Date", "Name", "IsRecurringYearly", "CreatedAt"),
            FakeResultSet.Empty("Id", "UserId", "StartDate", "EndDate", "Reason", "Notes", "CreatedAt"));

        var before = DateTime.UtcNow;
        var response = await context.CreateHandler().Handle(new GetUpcomingCalendarEventsQuery(1), CancellationToken.None);
        _ = before;

        response.IsSuccess.Should().BeTrue();
        var fromParam = (DateTime)context.ConnectionFactory.CapturedParameters["From"]!;
        var toParam = (DateTime)context.ConnectionFactory.CapturedParameters["To"]!;
        (toParam - fromParam).TotalDays.Should().BeApproximately(1, 0.01);
    }

    /// <summary>
    /// <c>days=31</c> (the maximum) is accepted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenDaysIsThirtyOne_ShouldSucceed()
    {
        var context = new GetUpcomingTestContext();
        context.ConnectionFactory.SetResults(
            context.CalendarCompanyRow(),
            FakeResultSet.Empty("Id", "SeriesMasterId", "OriginalStartUtc", "CalendarId", "CalendarName",
                "CalendarColor", "OwnerUserId", "OwnerName", "Title", "Summary", "Location", "VideoCallUrl",
                "IsAllDay", "StartUtc", "EndUtc", "RecurrenceRule", "RecurrenceEndUtc",
                "ActivityCompanyId", "ActivityCode", "ActivityName", "ActivityColor", "AllowsOverlap",
                "StatusCompanyId", "StatusCode", "StatusName", "StatusColor", "BlocksTime", "IsFinal",
                "ChannelCode", "OriginType", "OriginIdentifier", "OriginUserId"),
            FakeResultSet.Empty("Id", "Date", "Name", "IsRecurringYearly", "CreatedAt"),
            FakeResultSet.Empty("Id", "UserId", "StartDate", "EndDate", "Reason", "Notes", "CreatedAt"));

        var response = await context.CreateHandler().Handle(new GetUpcomingCalendarEventsQuery(31), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Holds the shared mocks for the upcoming query tests. Always seeds the CalendarCompany row.
    /// </summary>
    private sealed class GetUpcomingTestContext
    {
        public GetUpcomingTestContext()
        {
            CompanyId = Guid.NewGuid();
            ActorUserId = Guid.NewGuid();

            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(CompanyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(ActorUserId.ToString());
        }

        public FakeResultSet CalendarCompanyRow() => FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["Id"] = Guid.NewGuid(),
            ["TimeZone"] = TimeZoneId,
            ["IsOpen24Hours"] = true,
            ["BusinessHoursStart"] = null,
            ["BusinessHoursEnd"] = null
        });

        public Guid CompanyId { get; }
        public Guid ActorUserId { get; }

        public FakeSqlConnectionFactory ConnectionFactory { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<ICalendarRecurrenceExpander> ExpanderMock { get; } = new();
        public GetUpcomingCalendarEventsQueryHandler CreateHandler()
        {
        ExpanderMock.Setup(x => x.ValidateRule(It.IsAny<string>())).Returns((string r) => CalendarRecurrenceRule.Parse(r));
            var reader = new CalendarQueryReader(ExpanderMock.Object);
            var service = new CalendarEventQueryService(ConnectionFactory, reader, CurrentUserServiceMock.Object);
            return new GetUpcomingCalendarEventsQueryHandler(service);
        }
    }
}
