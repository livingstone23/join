using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelCalendars;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UseCases.Calendars.UserCalendars.Queries.GetMyCalendars;
using JOIN.Application.UnitTest.Common.TestDoubles;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelCalendars;

/// <summary>
/// SPEC 44 F10 panel — <c>GET /CalendarPanel/calendars</c>: at most 20 user ids per request, otherwise
/// <c>CALENDAR_TOO_MANY_USERS</c>. Tenant isolation comes from the <c>WHERE CompanyId = @TenantId</c>
/// filter that the shared <see cref="GetMyCalendarsQueryHandler.CalendarsSql"/> already enforces.
/// </summary>
public sealed class GetCalendarPanelCalendarsQueryHandlerTests
{
    private static readonly string[] CalendarColumns =
    {
        "Id", "OwnerUserId", "OwnerName", "Name", "Description", "Color",
        "IsActive", "SortOrder", "IsDefault", "CreatedAt"
    };

    /// <summary>
    /// Missing <c>CompanyId</c> short-circuits with <c>COMPANY_REQUIRED</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var ctx = new GetCalendarPanelCalendarsTestContext();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var response = await ctx.Handler.Handle(new GetCalendarPanelCalendarsQuery(UserIds: new[] { Guid.NewGuid() }), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    /// <summary>
    /// Empty user ids fail fast with <c>CALENDAR_TOO_MANY_USERS</c> (the source treats 0 as "out of the
    /// accepted [1, 20] range").
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserIdsIsEmpty_ShouldReturnTooManyUsersError()
    {
        var ctx = new GetCalendarPanelCalendarsTestContext();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(ctx.CompanyId);

        var response = await ctx.Handler.Handle(new GetCalendarPanelCalendarsQuery(UserIds: Array.Empty<Guid>()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.TooManyUsers);
    }

    /// <summary>
    /// Twenty user ids is the upper bound the panel accepts; Dapper is called once with both user ids
    /// deduplicated and the tenant filter.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserIdsAreAtMaxPanelUsers_ShouldReturnDeduplicatedCalendars()
    {
        var ctx = new GetCalendarPanelCalendarsTestContext();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(ctx.CompanyId);
        var owners = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToArray();
        var calendarId = Guid.NewGuid();
        ctx.ConnectionFactory.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["Id"] = calendarId,
            ["OwnerUserId"] = owners[0],
            ["OwnerName"] = "Owner One",
            ["Name"] = "Calendar",
            ["Description"] = null,
            ["Color"] = "#3b82f6",
            ["IsActive"] = true,
            ["SortOrder"] = 0,
            ["IsDefault"] = true,
            ["CreatedAt"] = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        }));
        // The Distinct() in the source takes the unique user ids; duplicate one to confirm dedup before binding.
        var withDuplicate = owners.Concat(new[] { owners[0] }).ToArray();

        var response = await ctx.Handler.Handle(new GetCalendarPanelCalendarsQuery(UserIds: withDuplicate), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Should().HaveCount(1);
        response.Data.First().OwnerName.Should().Be("Owner One");
        ctx.ConnectionFactory.LastCommandText.Should().Contain("OwnerUserId = ANY");
        // Dapper expands arrays into one parameter per value on non-Npgsql connections (Owners1..Owners20);
        // against PostgreSQL the same Guid[] goes as a single array parameter (verified in the integration tests).
        ctx.ConnectionFactory.CapturedParameters.Keys.Count(k => k.StartsWith("Owners", StringComparison.OrdinalIgnoreCase)).Should().Be(20);
    }

    /// <summary>
    /// Twenty-one user ids overflow the panel limit and return <c>CALENDAR_TOO_MANY_USCERS</c>;
    /// Dapper is never called.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserIdsExceedMaxPanelUsers_ShouldReturnTooManyUsersError()
    {
        var ctx = new GetCalendarPanelCalendarsTestContext();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(ctx.CompanyId);
        var owners = Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToArray();

        var response = await ctx.Handler.Handle(new GetCalendarPanelCalendarsQuery(UserIds: owners), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.TooManyUsers);
        ctx.ConnectionFactory.LastCommandText.Should().BeEmpty();
    }

    /// <summary>
    /// Reusable mocks for the panel calendars query.
    /// </summary>
    private sealed class GetCalendarPanelCalendarsTestContext
    {
        public GetCalendarPanelCalendarsTestContext() => CompanyId = Guid.NewGuid();

        public Guid CompanyId { get; }

        public FakeSqlConnectionFactory ConnectionFactory { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public GetCalendarPanelCalendarsQueryHandler Handler =>
            new(ConnectionFactory, CurrentUserServiceMock.Object);
    }
}
