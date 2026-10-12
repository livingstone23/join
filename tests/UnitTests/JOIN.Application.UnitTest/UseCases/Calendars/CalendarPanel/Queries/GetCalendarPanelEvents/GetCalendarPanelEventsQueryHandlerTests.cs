using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelEvents;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UnitTest.Common.TestDoubles;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelEvents;

/// <summary>
/// SPEC 44 F10 panel — <c>GET /CalendarPanel/events</c>. The handler is a thin seamed wrapper around
/// <see cref="CalendarEventQueryService"/>: it deduplicates the user ids, enforces the [1, 20] range and
/// passes the time window plus filters through. Each occurrence returned by the seam carries the
/// <c>OwnerUserId</c> and <c>OwnerName</c> so the panel can attribute one row per owner.
/// </summary>
public sealed class GetCalendarPanelEventsQueryHandlerTests
{
    private static readonly string[] CalendarCompanyColumns =
    {
        "Id", "TimeZone", "IsOpen24Hours", "BusinessHoursStart", "BusinessHoursEnd"
    };

    /// <summary>
    /// Empty user ids fail with <c>CALENDAR_TOO_MANY_USERS</c>; the seam is not called.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserIdsIsEmpty_ShouldReturnTooManyUsersError()
    {
        var ctx = new GetCalendarPanelEventsTestContext();

        var response = await ctx.Handler.Handle(
            new GetCalendarPanelEventsQuery(UserIds: Array.Empty<Guid>(), From: DateTimeOffset.UtcNow, To: DateTimeOffset.UtcNow.AddDays(1)),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.TooManyUsers);
        ctx.ConnectionFactory.LastCommandText.Should().BeEmpty();
    }

    /// <summary>
    /// Twenty-one user ids exceed the panel limit and return <c>CALENDAR_TOO_MANY_USERS</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserIdsExceedMaxPanelUsers_ShouldReturnTooManyUsersError()
    {
        var ctx = new GetCalendarPanelEventsTestContext();
        var owners = Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToArray();

        var response = await ctx.Handler.Handle(
            new GetCalendarPanelEventsQuery(UserIds: owners, From: DateTimeOffset.UtcNow, To: DateTimeOffset.UtcNow.AddDays(1)),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.TooManyUsers);
    }

    /// <summary>
    /// A range above 62 days is rejected by the <see cref="CalendarEventQueryService"/> shared with
    /// <c>CalendarEvents</c>; the panel surfaces the same <c>CALENDAR_RANGE_TOO_LARGE</c> error.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRangeExceeds62Days_ShouldReturnRangeTooLargeError()
    {
        var ctx = new GetCalendarPanelEventsTestContext();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(ctx.CompanyId);
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(ctx.ActorUserId.ToString());
        var owners = new[] { Guid.NewGuid() };
        var from = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(63);

        var response = await ctx.Handler.Handle(
            new GetCalendarPanelEventsQuery(UserIds: owners, From: from, To: to),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.RangeTooLarge);
    }

    /// <summary>
    /// Reusable mocks for the panel events query. The Dapper fake is wired to the real
    /// <see cref="CalendarEventQueryService"/> so the panel handler's delegation is exercised end-to-end.
    /// </summary>
    private sealed class GetCalendarPanelEventsTestContext
    {
        public GetCalendarPanelEventsTestContext()
        {
            CompanyId = Guid.NewGuid();
            ActorUserId = Guid.NewGuid();

            // Reader needs an expander for the in-memory expansion; the empty result set keeps its calls no-ops.
            var expanderMock = new Mock<ICalendarRecurrenceExpander>();
            QueryReader = new CalendarQueryReader(expanderMock.Object);
            QueryService = new CalendarEventQueryService(ConnectionFactory, QueryReader, CurrentUserServiceMock.Object);

            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(ActorUserId.ToString());
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(CompanyId);

            // Seed an empty CalendarCompany row so queryService.GetRangeAsync can run deep enough to expose
            // either the success shape or the range/company-validation responses under test.
            ConnectionFactory.SetResults(FakeResultSet.Empty(CalendarCompanyColumns));
        }

        public Guid CompanyId { get; }
        public Guid ActorUserId { get; }

        public FakeSqlConnectionFactory ConnectionFactory { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public CalendarQueryReader QueryReader { get; }
        public CalendarEventQueryService QueryService { get; }

        public GetCalendarPanelEventsQueryHandler Handler => new(QueryService);
    }
}
