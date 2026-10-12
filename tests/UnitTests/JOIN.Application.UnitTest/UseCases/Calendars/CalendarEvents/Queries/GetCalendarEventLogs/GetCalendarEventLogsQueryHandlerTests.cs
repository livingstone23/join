using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Queries.GetCalendarEventLogs;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEvents.Queries.GetCalendarEventLogs;

/// <summary>
/// SPEC 44 F10 / CalendarEvents group — logs query. Returns the log rows of the activity and of its exceptions,
/// in the order produced by the SQL. R2 keeps another user's activity invisible (404).
/// </summary>
public sealed class GetCalendarEventLogsQueryHandlerTests
{
    private static readonly Fixture _fixture = new();
    private const string TimeZoneId = "America/Managua";

    /// <summary>
    /// Logs of the activity are returned in ascending order of <c>OccurredUtc</c> (the SQL orders by
    /// <c>l.OccurredUtc, l.Created</c>). The DTOs carry the channel code, status names and notes.
    /// </summary>
    [Fact]
    public async Task Handle_WhenLogsExist_ShouldReturnOrderedByOccurredUtc()
    {
        var context = new GetLogsTestContext();
        var eventId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var channelCode = "WEB";
        var newStatusId = Guid.NewGuid();

        // GetLogsAsync never reads the calendar company (the R2 owner check uses event ownership only);
        // the two grids are the event row and the logs SQL, in that order.
        context.ConnectionFactory.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["Id"] = eventId,
                ["SeriesMasterId"] = null,
                ["OriginalStartUtc"] = null,
                ["CalendarId"] = Guid.NewGuid(),
                ["CalendarName"] = "Mi calendario",
                ["CalendarColor"] = "#1E88E5",
                ["OwnerUserId"] = context.ActorUserId,
                ["OwnerName"] = "Ada Lovelace",
                ["Title"] = "Sync",
                ["Summary"] = null,
                ["Location"] = null,
                ["VideoCallUrl"] = null,
                ["IsAllDay"] = false,
                ["StartUtc"] = DateTime.SpecifyKind(new DateTime(2026, 6, 15, 14, 0, 0), DateTimeKind.Utc),
                ["EndUtc"] = DateTime.SpecifyKind(new DateTime(2026, 6, 15, 15, 0, 0), DateTimeKind.Utc),
                ["RecurrenceRule"] = null,
                ["RecurrenceEndUtc"] = null,
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
                ["ChannelCode"] = channelCode,
                ["OriginType"] = (int)CalendarEventOriginType.User,
                ["OriginIdentifier"] = null,
                ["OriginUserId"] = actorId,
                ["Description"] = null,
                ["RequesterPersonId"] = null,
                ["CustomerId"] = null,
                ["TicketId"] = null,
                ["ProjectId"] = null,
                ["StatusChangedUtc"] = null,
                ["StatusChangedByUserId"] = null,
                ["CreatedAt"] = DateTime.UtcNow
            }),
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = Guid.NewGuid(),
                    ["CalendarEventId"] = eventId,
                    ["Action"] = (int)CalendarEventLogAction.Created,
                    ["PreviousStartUtc"] = null,
                    ["PreviousEndUtc"] = null,
                    ["NewStartUtc"] = DateTime.SpecifyKind(new DateTime(2026, 6, 15, 14, 0, 0), DateTimeKind.Utc),
                    ["NewEndUtc"] = DateTime.SpecifyKind(new DateTime(2026, 6, 15, 15, 0, 0), DateTimeKind.Utc),
                    ["PreviousStatusCompanyId"] = null,
                    ["PreviousStatusName"] = null,
                    ["NewStatusCompanyId"] = newStatusId,
                    ["NewStatusName"] = "Pendiente",
                    ["ChannelCode"] = channelCode,
                    ["ActorUserId"] = actorId,
                    ["ActorName"] = "Ada Lovelace",
                    ["ActorIdentifier"] = null,
                    ["Notes"] = null,
                    ["OccurredUtc"] = DateTime.SpecifyKind(new DateTime(2026, 6, 15, 13, 59, 0), DateTimeKind.Utc)
                },
                new Dictionary<string, object?>
                {
                    ["Id"] = Guid.NewGuid(),
                    ["CalendarEventId"] = eventId,
                    ["Action"] = (int)CalendarEventLogAction.Updated,
                    ["PreviousStartUtc"] = null,
                    ["PreviousEndUtc"] = null,
                    ["NewStartUtc"] = null,
                    ["NewEndUtc"] = null,
                    ["PreviousStatusCompanyId"] = null,
                    ["PreviousStatusName"] = null,
                    ["NewStatusCompanyId"] = null,
                    ["NewStatusName"] = null,
                    ["ChannelCode"] = channelCode,
                    ["ActorUserId"] = actorId,
                    ["ActorName"] = "Ada Lovelace",
                    ["ActorIdentifier"] = null,
                    ["Notes"] = "title edited",
                    ["OccurredUtc"] = DateTime.SpecifyKind(new DateTime(2026, 6, 15, 14, 30, 0), DateTimeKind.Utc)
                }));

        var response = await context.CreateHandler().Handle(new GetCalendarEventLogsQuery(eventId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().HaveCount(2);
        response.Data![0].Action.Should().Be(CalendarEventLogAction.Created.ToString());
        response.Data[0].OccurredUtc.Should().BeBefore(response.Data[1].OccurredUtc);
        response.Data[1].Action.Should().Be(CalendarEventLogAction.Updated.ToString());
        response.Data[1].Notes.Should().Be("title edited");
    }

    /// <summary>
    /// When the event is not found (or belongs to another user), the response is
    /// <c>CALENDAR_EVENT_NOT_FOUND</c> and the logs SQL is not run.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEventIsMissingOrNotOwned_ShouldReturnEventNotFound()
    {
        var context = new GetLogsTestContext();
        // Empty event result set; GetLogsAsync never reads Calendar.CalendarCompanies for the log path.
        context.ConnectionFactory.SetResults(
            FakeResultSet.Empty("Id", "SeriesMasterId", "OriginalStartUtc", "CalendarId", "CalendarName",
                "CalendarColor", "OwnerUserId", "OwnerName", "Title", "Summary", "Location", "VideoCallUrl",
                "IsAllDay", "StartUtc", "EndUtc", "RecurrenceRule", "RecurrenceEndUtc",
                "ActivityCompanyId", "ActivityCode", "ActivityName", "ActivityColor", "AllowsOverlap",
                "StatusCompanyId", "StatusCode", "StatusName", "StatusColor", "BlocksTime", "IsFinal",
                "ChannelCode", "OriginType", "OriginIdentifier", "OriginUserId",
                "Description", "RequesterPersonId", "CustomerId", "TicketId", "ProjectId",
                "StatusChangedUtc", "StatusChangedByUserId", "CreatedAt"));

        var response = await context.CreateHandler().Handle(new GetCalendarEventLogsQuery(Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.EventNotFound);
    }

    /// <summary>
    /// Holds the shared mocks for the logs query tests. Always seeds the CalendarCompany row.
    /// </summary>
    private sealed class GetLogsTestContext
    {
        public GetLogsTestContext()
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
        public GetCalendarEventLogsQueryHandler CreateHandler()
        {
        ExpanderMock.Setup(x => x.ValidateRule(It.IsAny<string>())).Returns((string r) => CalendarRecurrenceRule.Parse(r));
            var reader = new CalendarQueryReader(ExpanderMock.Object);
            var service = new CalendarEventQueryService(ConnectionFactory, reader, CurrentUserServiceMock.Object);
            return new GetCalendarEventLogsQueryHandler(service);
        }
    }
}
