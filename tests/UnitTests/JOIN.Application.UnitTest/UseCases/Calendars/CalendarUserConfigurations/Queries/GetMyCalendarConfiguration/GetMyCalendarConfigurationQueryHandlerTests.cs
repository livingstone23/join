using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Common;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Queries.GetMyCalendarConfiguration;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarUserConfigurations.Queries.GetMyCalendarConfiguration;

/// <summary>
/// Contains the unit tests for <see cref="GetMyCalendarConfigurationQueryHandler"/>: the user-resolution guard and
/// the happy path that wraps the Dapper <see cref="JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.CalendarUserConfigurationQuery"/>
/// read.
/// </summary>
public sealed class GetMyCalendarConfigurationQueryHandlerTests
{
    private static readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the user-resolution guard when the JWT does not carry a parseable user id.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserIdIsUnparseable_ShouldReturnUserRequiredError()
    {
        var context = new GetMyCalendarConfigurationQueryTestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns("not-a-guid");
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(_fixture.Create<Guid>());

        var response = await context.CreateHandler().Handle(new GetMyCalendarConfigurationQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.UserRequired);
        response.Errors.Should().Contain("The authenticated user could not be resolved.");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Verifies the company-not-configured branch when the first Dapper read returns no row.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyHasNoConfiguration_ShouldReturnCompanyNotConfiguredError()
    {
        var companyId = _fixture.Create<Guid>();
        var userId = _fixture.Create<Guid>();
        var context = new GetMyCalendarConfigurationQueryTestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(userId.ToString());
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        // The first Dapper read (CalendarCompanyRow) sees an empty result; the handler short-circuits.
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "TimeZone",
                "IsOpen24Hours",
                "BusinessHoursStart",
                "BusinessHoursEnd"));

        var response = await context.CreateHandler().Handle(new GetMyCalendarConfigurationQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
    }

    /// <summary>
    /// Verifies the happy path: with a valid user id and a saved configuration the DTO carries the effective hours
    /// and the company context the frontend needs.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserHasConfiguration_ShouldReturnEnrichedDto()
    {
        var companyId = _fixture.Create<Guid>();
        var userId = _fixture.Create<Guid>();
        var configId = Guid.NewGuid();
        var calendarId = Guid.NewGuid();
        var context = new GetMyCalendarConfigurationQueryTestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(userId.ToString());
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        // The CalendarUserConfigurationQuery issues two sequential QueryFirstOrDefaultAsync calls; the
        // FakeDbDataReader starts at the first result set for each command. Both calls see the same row, which
        // therefore has to carry every column the two DTOs consume (Dapper ignores extras).
        context.Connection.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["Id"] = configId,
                ["TimeZone"] = "America/Managua",
                ["IsOpen24Hours"] = false,
                ["BusinessHoursStart"] = new TimeOnly(8, 0),
                ["BusinessHoursEnd"] = new TimeOnly(17, 0),
                ["UserId"] = userId,
                ["UserName"] = "Alice Smith",
                ["Email"] = "alice@acme.test",
                ["HasCalendar"] = true,
                ["DefaultCalendarId"] = calendarId,
                ["DefaultCalendarName"] = "Personal",
                ["WorkDays"] = 62,
                ["DefaultView"] = 2,
                ["WeekStartsOn"] = 1
            }));

        var response = await context.CreateHandler().Handle(new GetMyCalendarConfigurationQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.UserId.Should().Be(userId);
        response.Data.UserName.Should().Be("Alice Smith");
        response.Data.HasCalendar.Should().BeTrue();
        response.Data.EffectiveBusinessHoursStart.Should().Be(new TimeOnly(8, 0));
        response.Data.EffectiveBusinessHoursEnd.Should().Be(new TimeOnly(17, 0));
        response.Data.TimeZone.Should().Be("America/Managua");
        response.Data.CompanyIsOpen24Hours.Should().BeFalse();
        response.Data.CompanyBusinessHoursStart.Should().Be(new TimeOnly(8, 0));
        response.Data.CompanyBusinessHoursEnd.Should().Be(new TimeOnly(17, 0));
    }

    /// <summary>
    /// Holds the reusable mocks for the get-my query tests. The <see cref="CalendarQueryReader"/> is instantiated
    /// directly and the connection factory hands out a <see cref="FakeDbConnection"/> so the Dapper read is
    /// deterministic.
    /// </summary>
    private sealed class GetMyCalendarConfigurationQueryTestContext
    {
        public GetMyCalendarConfigurationQueryTestContext()
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetMyCalendarConfigurationQueryHandler CreateHandler()
        {
            var reader = new CalendarQueryReader(ConnectionFactoryMock.Object, new NoopExpander());
            var query = new JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.CalendarUserConfigurationQuery(
                ConnectionFactoryMock.Object,
                reader,
                CurrentUserServiceMock.Object);
            return new GetMyCalendarConfigurationQueryHandler(query, CurrentUserServiceMock.Object);
        }
    }

    /// <summary>
    /// Test double for the recurrence expander: it never produces occurrences (the affected-events tests live in the
    /// create-period suite). Keeps the get-my query isolated from the Ical.Net dependency.
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
