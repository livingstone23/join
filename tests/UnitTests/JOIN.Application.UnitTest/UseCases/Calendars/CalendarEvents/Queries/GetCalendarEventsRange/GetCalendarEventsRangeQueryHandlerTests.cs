using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Queries.GetCalendarEventsRange;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEvents.Queries.GetCalendarEventsRange;

/// <summary>
/// SPEC 44 F10 / CalendarEvents group — range query. Covers the happy path with one master and one exception
/// (the exception replaces the occurrence in the expansion), the non-working days + owner periods are
/// returned as <c>NonWorkingDays</c>, and the range-too-large error.
/// </summary>
public sealed class GetCalendarEventsRangeQueryHandlerTests
{
    private static readonly Fixture _fixture = new();
    private const string TimeZoneId = "America/Managua";

    /// <summary>
    /// Range expansion applies exceptions: the exception overrides the master occurrence at
    /// <c>2026-06-16</c> in the response, and the master is rendered for <c>2026-06-15</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSeriesHasException_ShouldReplaceOccurrenceInResponse()
    {
        var context = new GetRangeTestContext();
        var masterId = Guid.NewGuid();
        var exceptionId = Guid.NewGuid();
        var day1 = DateTime.SpecifyKind(new DateTime(2026, 6, 15, 14, 0, 0), DateTimeKind.Utc);
        var day2 = DateTime.SpecifyKind(new DateTime(2026, 6, 16, 14, 0, 0), DateTimeKind.Utc);

        // CalendarCompany + Range SQL (master) + ExceptionsSql + Holidays + Periods.
        context.ConnectionFactory.SetResults(
            context.CalendarCompanyRow(),
            FakeResultSet.FromRows(EventRow(masterId, null, "Daily", day1, day1.AddHours(1), "FREQ=DAILY;COUNT=5")),
            FakeResultSet.FromRows(EventRow(exceptionId, masterId, "Replaced", day2, day2.AddHours(1), null,
                originalStartUtc: day2)),
            // Non-working days: a single holiday on 2026-06-17.
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["Date"] = new DateTime(2026, 6, 17),
                ["Name"] = "Father's Day",
                ["IsRecurringYearly"] = false,
                ["CreatedAt"] = DateTime.UtcNow,
                // OccursOn is the date Dapper maps onto the DTO; it equals Date for non-recurring holidays.
                ["OccursOn"] = new DateTime(2026, 6, 17)
            }),
            FakeResultSet.Empty("Id", "UserId", "StartDate", "EndDate", "Reason", "Notes", "CreatedAt"));

        // The expander returns the two occurrences of the master within the range; the
        // exception key matches day2 and is filtered out by CalendarOccurrenceExpansion.
        context.ExpanderMock
            .Setup(x => x.Expand(It.IsAny<CalendarRecurrenceSeries>(),
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), TimeZoneId))
            .Returns((CalendarRecurrenceSeries s, DateTime from, DateTime to, string _) =>
            {
                var occurrences = new List<CalendarOccurrence>();
                for (var day = 0; day < 3; day++)
                {
                    var start = day1.AddDays(day);
                    occurrences.Add(new CalendarOccurrence(start, start.AddHours(1)));
                }

                return occurrences.Where(o => o.StartUtc >= from && o.StartUtc < to).ToList();
            });

        var query = new GetCalendarEventsRangeQuery(
            new DateTimeOffset(day1, TimeSpan.Zero),
            new DateTimeOffset(day1.AddDays(3), TimeSpan.Zero));

        var response = await context.CreateHandler().Handle(query, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Occurrences.Should().HaveCount(3);
        response.Data.Occurrences.Should().Contain(o => o.ExceptionId == exceptionId && o.Title == "Replaced");
        response.Data.Occurrences.Count(o => o.EventId == masterId && o.Title == "Daily").Should().Be(2);
        response.Data.NonWorkingDays.Should().ContainSingle()
            .Which.Name.Should().Be("Father's Day");
    }

    /// <summary>
    /// The viewer's own non-working periods are returned with the reason in <c>Name</c> and the user id.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserHasNonWorkingPeriod_ShouldReturnReasonForOwnersPeriod()
    {
        var context = new GetRangeTestContext();
        var viewerId = context.ActorUserId;
        var day1 = DateTime.SpecifyKind(new DateTime(2026, 6, 15, 14, 0, 0), DateTimeKind.Utc);

        context.ConnectionFactory.SetResults(
            context.CalendarCompanyRow(),
            FakeResultSet.Empty("Id", "SeriesMasterId", "OriginalStartUtc", "CalendarId", "CalendarName",
                "CalendarColor", "OwnerUserId", "OwnerName", "Title", "Summary", "Location", "VideoCallUrl",
                "IsAllDay", "StartUtc", "EndUtc", "RecurrenceRule", "RecurrenceEndUtc",
                "ActivityCompanyId", "ActivityCode", "ActivityName", "ActivityColor", "AllowsOverlap",
                "StatusCompanyId", "StatusCode", "StatusName", "StatusColor", "BlocksTime", "IsFinal",
                "ChannelCode", "OriginType", "OriginIdentifier", "OriginUserId"),
            FakeResultSet.Empty("Id", "SeriesMasterId", "OriginalStartUtc", "CalendarId", "CalendarName",
                "CalendarColor", "OwnerUserId", "OwnerName", "Title", "Summary", "Location", "VideoCallUrl",
                "IsAllDay", "StartUtc", "EndUtc", "RecurrenceRule", "RecurrenceEndUtc",
                "ActivityCompanyId", "ActivityCode", "ActivityName", "ActivityColor", "AllowsOverlap",
                "StatusCompanyId", "StatusCode", "StatusName", "StatusColor", "BlocksTime", "IsFinal",
                "ChannelCode", "OriginType", "OriginIdentifier", "OriginUserId"),
            FakeResultSet.Empty("Id", "Date", "Name", "IsRecurringYearly", "CreatedAt"),
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["UserId"] = viewerId,
                ["StartDate"] = new DateTime(2026, 6, 16),
                ["EndDate"] = new DateTime(2026, 6, 17),
                ["Reason"] = 1, // CalendarNonWorkingReason.Vacation
                ["Notes"] = "vacaciones",
                ["CreatedAt"] = DateTime.UtcNow,
                // The DTO has UserName; the production SQL populates it from the joined user. The view layer never
                // surfaces it for "other users" so we only need a placeholder here, but Dapper requires the column.
                ["UserName"] = "User Name"
            }));

        var query = new GetCalendarEventsRangeQuery(
            new DateTimeOffset(day1, TimeSpan.Zero),
            new DateTimeOffset(day1.AddDays(7), TimeSpan.Zero));

        var response = await context.CreateHandler().Handle(query, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var owner = response.Data!.NonWorkingDays.Single();
        owner.Kind.Should().Be("NonWorkingPeriod");
        owner.Name.Should().NotBeNull();
        owner.UserId.Should().Be(viewerId);
    }

    /// <summary>
    /// A range larger than 62 days is rejected with <c>CALENDAR_RANGE_TOO_LARGE</c> before any SQL runs.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRangeExceedsLimit_ShouldReturnRangeTooLarge()
    {
        var context = new GetRangeTestContext();
        var from = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(63);

        var response = await context.CreateHandler().Handle(
            new GetCalendarEventsRangeQuery(from, to), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.RangeTooLarge);
    }

    /// <summary>
    /// A range shorter than 62 days with no activities returns an empty <c>Occurrences</c> list.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRangeIsEmpty_ShouldReturnEmptyOccurrences()
    {
        var context = new GetRangeTestContext();
        var day1 = DateTime.SpecifyKind(new DateTime(2026, 6, 15, 14, 0, 0), DateTimeKind.Utc);

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

        var query = new GetCalendarEventsRangeQuery(
            new DateTimeOffset(day1, TimeSpan.Zero),
            new DateTimeOffset(day1.AddDays(7), TimeSpan.Zero));

        var response = await context.CreateHandler().Handle(query, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Occurrences.Should().BeEmpty();
    }

    /// <summary>
    /// Holds the shared mocks for the range query tests. Uses <see cref="FakeSqlConnectionFactory"/> to feed
    /// canned rows to Dapper.
    /// </summary>
    private sealed class GetRangeTestContext
    {
        public GetRangeTestContext()
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
        public GetCalendarEventsRangeQueryHandler CreateHandler()
        {
        ExpanderMock.Setup(x => x.ValidateRule(It.IsAny<string>())).Returns((string r) => CalendarRecurrenceRule.Parse(r));
            var reader = new CalendarQueryReader(ConnectionFactory, ExpanderMock.Object);
            var service = new CalendarEventQueryService(ConnectionFactory, reader, CurrentUserServiceMock.Object);
            return new GetCalendarEventsRangeQueryHandler(service);
        }
    }

    private static Dictionary<string, object?> EventRow(
        Guid id,
        Guid? seriesMasterId,
        string title,
        DateTime startUtc,
        DateTime endUtc,
        string? recurrenceRule,
        DateTime? originalStartUtc = null) => new()
    {
        ["Id"] = id,
        ["SeriesMasterId"] = seriesMasterId,
        ["OriginalStartUtc"] = originalStartUtc,
        ["CalendarId"] = Guid.NewGuid(),
        ["CalendarName"] = "Mi calendario",
        ["CalendarColor"] = "#1E88E5",
        ["OwnerUserId"] = Guid.NewGuid(),
        ["OwnerName"] = "Ada Lovelace",
        ["Title"] = title,
        ["Summary"] = null,
        ["Location"] = null,
        ["VideoCallUrl"] = null,
        ["IsAllDay"] = false,
        ["StartUtc"] = startUtc,
        ["EndUtc"] = endUtc,
        ["RecurrenceRule"] = recurrenceRule,
        ["RecurrenceEndUtc"] = recurrenceRule is null ? null : startUtc.AddDays(5),
        ["ActivityCompanyId"] = Guid.NewGuid(),
        ["ActivityCode"] = CalendarCodes.Activities.Meeting,
        ["ActivityName"] = "Reunión",
        ["ActivityColor"] = "#1E88E5",
        ["AllowsOverlap"] = false,
        ["StatusCompanyId"] = Guid.NewGuid(),
        ["StatusCode"] = CalendarCodes.Statuses.Pending,
        ["StatusName"] = "Pendiente",
        ["StatusColor"] = "#FB8C00",
        ["BlocksTime"] = true,
        ["IsFinal"] = false,
        ["ChannelCode"] = CommunicationChannelCodes.Web,
        ["OriginType"] = (int)CalendarEventOriginType.User,
        ["OriginIdentifier"] = null,
        ["OriginUserId"] = Guid.NewGuid()
    };
}
