using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelAvailability;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UnitTest.Common.TestDoubles;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelAvailability;

/// <summary>
/// SPEC 44 F10 panel — <c>GET /CalendarPanel/availability</c>. Single-user availability restricted to 14 days,
/// enforcing R4 (overlap, when the type forbids overlap) and R5 (work day, non-working period, holiday,
/// business hour, when the type requires business hours). Free slots are produced in blocks of the activity
/// type duration through <see cref="CalendarAvailabilityCalculator"/>.
/// </summary>
public sealed class GetCalendarPanelAvailabilityQueryHandlerTests
{
    private static readonly string[] CalendarCompanyColumns =
    {
        "Id", "TimeZone", "IsOpen24Hours", "BusinessHoursStart", "BusinessHoursEnd"
    };

    private static readonly string[] UserRowColumns =
    {
        "ConfigurationId", "WorkDays", "BusinessHoursStart", "BusinessHoursEnd"
    };

    private static readonly string[] ActivityRowColumns =
    {
        "DefaultDurationMinutes", "AllowsOverlap", "RequiresBusinessHours"
    };

    /// <summary>
    /// Without <c>CompanyId</c> the handler answers <c>COMPANY_REQUIRED</c> without touching the reader.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var ctx = new GetCalendarPanelAvailabilityTestContext();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var response = await ctx.Handler.Handle(ctx.BuildQuery(ctx.UserId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    /// <summary>
    /// The To parameter must be strictly after From and within 14 days; both branches fail fast with
    /// <c>CALENDAR_RANGE_TOO_LARGE</c>.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    public async Task Handle_WhenRangeExceedsLimit_ShouldReturnRangeTooLargeError(int addDays)
    {
        var ctx = new GetCalendarPanelAvailabilityTestContext();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(ctx.CompanyId);
        var from = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(addDays);

        var response = await ctx.Handler.Handle(ctx.BuildQuery(ctx.UserId, from, to), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.RangeTooLarge);
    }

    /// <summary>
    /// Missing calendar company configuration for the tenant yields <c>CALENDAR_COMPANY_NOT_CONFIGURED</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIsNotConfigured_ShouldReturnCompanyNotConfiguredError()
    {
        var ctx = new GetCalendarPanelAvailabilityTestContext();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(ctx.CompanyId);
        // The handler makes two Dapper calls in this branch: GetCalendarCompanyAsync then GetUsers row.
        // The company query returns empty → caller short-circuits, so the second read is never attempted.
        ctx.ConnectionFactory.SetResults(FakeResultSet.Empty(CalendarCompanyColumns));

        var response = await ctx.Handler.Handle(ctx.BuildQuery(ctx.UserId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
    }

    /// <summary>
    /// When the user is not a member of the current company the SQL by-user join returns no row, and the
    /// handler answers <c>CALENDAR_USER_NOT_IN_COMPANY</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserIsNotInCompany_ShouldReturnUserNotInCompanyError()
    {
        var ctx = new GetCalendarPanelAvailabilityTestContext();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(ctx.CompanyId);
        ctx.ConnectionFactory.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["TimeZone"] = "UTC",
                ["IsOpen24Hours"] = true,
                ["BusinessHoursStart"] = null,
                ["BusinessHoursEnd"] = null
            }),
            FakeResultSet.Empty(UserRowColumns));

        var response = await ctx.Handler.Handle(ctx.BuildQuery(ctx.UserId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.UserNotInCompany);
    }

    /// <summary>
    /// User member but no <c>Calendar.UserConfigurations</c> row in the company → <c>CALENDAR_USER_HAS_NO_CALENDAR</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserHasNoConfiguration_ShouldReturnUserHasNoCalendarError()
    {
        var ctx = new GetCalendarPanelAvailabilityTestContext();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(ctx.CompanyId);
        ctx.ConnectionFactory.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["TimeZone"] = "UTC",
                ["IsOpen24Hours"] = true,
                ["BusinessHoursStart"] = null,
                ["BusinessHoursEnd"] = null
            }),
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["ConfigurationId"] = null,
                ["WorkDays"] = 31,
                ["BusinessHoursStart"] = null,
                ["BusinessHoursEnd"] = null
            }));

        var response = await ctx.Handler.Handle(ctx.BuildQuery(ctx.UserId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.UserHasNoCalendar);
    }

    /// <summary>
    /// When the activity type is not active in the company the handler returns <c>CALENDAR_ACTIVITY_NOT_ENABLED</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenActivityIsNotEnabled_ShouldReturnActivityNotEnabledError()
    {
        var ctx = new GetCalendarPanelAvailabilityTestContext();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(ctx.CompanyId);
        ctx.ConnectionFactory.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["TimeZone"] = "UTC",
                ["IsOpen24Hours"] = true,
                ["BusinessHoursStart"] = null,
                ["BusinessHoursEnd"] = null
            }),
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["ConfigurationId"] = Guid.NewGuid(),
                ["WorkDays"] = 31,
                ["BusinessHoursStart"] = null,
                ["BusinessHoursEnd"] = null
            }),
            FakeResultSet.Empty(ActivityRowColumns));

        var response = await ctx.Handler.Handle(ctx.BuildQuery(ctx.UserId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.ActivityNotEnabled);
    }

    /// <summary>
    /// Reusable mocks for the availability query.
    /// </summary>
    private sealed class GetCalendarPanelAvailabilityTestContext
    {
        public GetCalendarPanelAvailabilityTestContext()
        {
            CompanyId = Guid.NewGuid();
            UserId = Guid.NewGuid();
            ActivityCompanyId = Guid.NewGuid();
            ExpanderMock = new Mock<ICalendarRecurrenceExpander>();
            Reader = new CalendarQueryReader(ConnectionFactory, ExpanderMock.Object);
        }

        public Guid CompanyId { get; }
        public Guid UserId { get; }
        public Guid ActivityCompanyId { get; }
        public Mock<ICalendarRecurrenceExpander> ExpanderMock { get; }

        public FakeSqlConnectionFactory ConnectionFactory { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public CalendarQueryReader Reader { get; }

        public GetCalendarPanelAvailabilityQueryHandler Handler =>
            new(ConnectionFactory, Reader, CurrentUserServiceMock.Object);

        public GetCalendarPanelAvailabilityQuery BuildQuery(
            Guid userId,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null) =>
            new(userId,
                from ?? new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                to ?? new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
                ActivityCompanyId);
    }
}
