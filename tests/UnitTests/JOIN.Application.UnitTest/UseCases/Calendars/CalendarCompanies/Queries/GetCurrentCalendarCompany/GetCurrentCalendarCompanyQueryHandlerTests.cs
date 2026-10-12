using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarCompanies.Queries.GetCurrentCalendarCompany;
using JOIN.Application.UseCases.Calendars.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarCompanies.Queries.GetCurrentCalendarCompany;

/// <summary>
/// Contains the unit tests for <see cref="GetCurrentCalendarCompanyQueryHandler"/>: tenant guard, the empty
/// <c>isConfigured=false</c> projection, and the configured projection with module/users counts.
/// </summary>
public sealed class GetCurrentCalendarCompanyQueryHandlerTests
{
    private static readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant context is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new GetCurrentCalendarCompanyQueryTestContext(Guid.Empty);

        var response = await context.CreateHandler().Handle(new GetCurrentCalendarCompanyQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        response.Errors.Should().Contain("The authenticated token must contain a valid CompanyId claim.");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Verifies that without a saved configuration the response is successful but carries
    /// <c>IsConfigured=false</c>, <c>ModuleEnabled=false</c>, and <c>UsersWithoutCalendar=0</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyHasNoConfiguration_ShouldReturnIsConfiguredFalse()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new GetCurrentCalendarCompanyQueryTestContext(companyId);
        // Dapper's GridReader auto-advances to the next non-empty result set when the current one is empty;
        // provide an extra set so the third ReadSingleAsync call finds a row instead of throwing.
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "DefaultActivityUserId",
                "DefaultActivityUserName",
                "TimeZone",
                "IsOpen24Hours",
                "BusinessHoursStart",
                "BusinessHoursEnd",
                "DefaultCalendarName",
                "RescheduledByOthersStatusCompanyId",
                "RescheduledByOthersStatusName"),
            FakeResultSet.FromScalar(0),
            FakeResultSet.FromScalar(0),
            FakeResultSet.FromScalar(0));

        var response = await context.CreateHandler().Handle(new GetCurrentCalendarCompanyQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.IsConfigured.Should().BeFalse();
        response.Data.ModuleEnabled.Should().BeFalse();
        response.Data.UsersWithoutCalendar.Should().Be(0);
        response.Data.Id.Should().BeNull();
        response.Data.TimeZone.Should().BeNull();
    }

    /// <summary>
    /// Verifies that with a saved configuration the response carries the projection fields, the module-enabled
    /// flag from the second query, and the number of users without a calendar from the third.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyHasConfiguration_ShouldReturnIsConfiguredTrue()
    {
        var companyId = _fixture.Create<Guid>();
        var configId = Guid.NewGuid();
        var statusId = Guid.NewGuid();
        var context = new GetCurrentCalendarCompanyQueryTestContext(companyId);
        context.Connection.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["Id"] = configId,
                ["DefaultActivityUserId"] = Guid.NewGuid(),
                ["DefaultActivityUserName"] = "Alice Smith",
                ["TimeZone"] = "America/Managua",
                ["IsOpen24Hours"] = false,
                ["BusinessHoursStart"] = new TimeOnly(8, 0),
                ["BusinessHoursEnd"] = new TimeOnly(17, 0),
                ["DefaultCalendarName"] = "Mi calendario",
                ["RescheduledByOthersStatusCompanyId"] = statusId,
                ["RescheduledByOthersStatusName"] = "Rescheduled"
            }),
            FakeResultSet.FromScalar(1),
            FakeResultSet.FromScalar(3));

        var response = await context.CreateHandler().Handle(new GetCurrentCalendarCompanyQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.IsConfigured.Should().BeTrue();
        response.Data.ModuleEnabled.Should().BeTrue();
        response.Data.UsersWithoutCalendar.Should().Be(3);
        response.Data.Id.Should().Be(configId);
        response.Data.TimeZone.Should().Be("America/Managua");
        response.Data.IsOpen24Hours.Should().BeFalse();
        response.Data.BusinessHoursStart.Should().Be(new TimeOnly(8, 0));
        response.Data.BusinessHoursEnd.Should().Be(new TimeOnly(17, 0));
        response.Data.DefaultCalendarName.Should().Be("Mi calendario");
        response.Data.RescheduledByOthersStatusCompanyId.Should().Be(statusId);
        response.Data.RescheduledByOthersStatusName.Should().Be("Rescheduled");
        response.Data.DefaultActivityUserName.Should().Be("Alice Smith");

        context.Connection.LastCommandText.Should().Contain("@TenantId");
        context.Connection.LastCommandText.Should().Contain("CalendarCompanies cc");
    }

    /// <summary>
    /// Holds the reusable mocks for the get-current query tests. Uses the shared <see cref="FakeDbConnection"/> so
    /// the Dapper multi-query path is exercised deterministically.
    /// </summary>
    private sealed class GetCurrentCalendarCompanyQueryTestContext
    {
        public GetCurrentCalendarCompanyQueryTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetCurrentCalendarCompanyQueryHandler CreateHandler() =>
            new GetCurrentCalendarCompanyQueryHandler(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
    }
}