using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.Tickets.Queries;
using JOIN.Application.UseCases.Messaging.Tickets.Queries.GetTicketSummary;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.GetTicketSummary;

/// <summary>
/// Unit tests for the SPEC 52 inbox summary handler. Verifies the four counts
/// are sourced from <c>TicketSlaCalculator</c> and the SQL is properly tenant
/// scoped.
/// </summary>
public sealed class GetTicketSummaryQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Tenant guard — same <c>COMPANY_REQUIRED</c> short-circuit every other
    /// tenant-scoped handler does.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new TestContext(companyId: Guid.Empty);
        var handler = context.CreateHandler();

        var response = await handler.Handle(new GetTicketSummaryQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Verifies the four counts the summary exposes: open, SLA breached,
    /// inactive and unassigned. Tickets in a final status (rows we never
    /// SELECT today) and tickets of another tenant (filtered by
    /// <c>t.CompanyId = @TenantId</c>) are excluded.
    /// </summary>
    [Fact]
    public async Task Handle_WhenOpenTicketsExist_ShouldAggregateAllFourCounts()
    {
        var companyId = _fixture.Create<Guid>();
        var createdAt = DateTime.UtcNow.AddHours(-200);
        var lastActivityAt = DateTime.UtcNow.AddDays(-30);
        var lastActivityRecent = DateTime.UtcNow.AddHours(-1);

        var context = new TestContext(companyId);
        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = Guid.NewGuid(),
                    ["Created"] = createdAt,
                    ["AssignedToUserId"] = null,
                    ["ResolutionTimeUnits"] = 3,
                    ["ComplexityTimeUnitCode"] = 24,
                    ["MaxDayTicketInactivity"] = 7,
                    ["LastActivityAt"] = lastActivityAt
                },
                new Dictionary<string, object?>
                {
                    ["Id"] = Guid.NewGuid(),
                    ["Created"] = DateTime.UtcNow.AddHours(-1),
                    ["AssignedToUserId"] = Guid.NewGuid(),
                    ["ResolutionTimeUnits"] = 1,
                    ["ComplexityTimeUnitCode"] = 24,
                    ["MaxDayTicketInactivity"] = 7,
                    ["LastActivityAt"] = lastActivityRecent
                },
                new Dictionary<string, object?>
                {
                    ["Id"] = Guid.NewGuid(),
                    ["Created"] = createdAt,
                    ["AssignedToUserId"] = Guid.NewGuid(),
                    ["ResolutionTimeUnits"] = 3,
                    ["ComplexityTimeUnitCode"] = 24,
                    ["MaxDayTicketInactivity"] = 7,
                    ["LastActivityAt"] = lastActivityAt
                }));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetTicketSummaryQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        // 3 rows scanned.
        response.Data!.OpenCount.Should().Be(3);
        // Row 0: SLA breached (created 200h ago, 3×24 = 72h due), unassigned, inactive (30 days > 7d threshold).
        // Row 1: recent, assigned, not breached, not inactive.
        // Row 2: SLA breached, assigned, inactive.
        response.Data.SlaBreachedCount.Should().Be(2);
        response.Data.InactiveCount.Should().Be(2);
        response.Data.UnassignedCount.Should().Be(1);
        response.Data.GeneratedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        // Final-status tickets are excluded at SQL level — the WHERE clause is the contract.
        context.Connection.LastCommandText.Should().Contain("t.CompanyId = @TenantId");
        context.Connection.LastCommandText.Should().Contain("t.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("ts.IsFinal = FALSE");
        context.Connection.CapturedParameters["TenantId"].Should().Be(companyId);
    }

    /// <summary>
    /// Companies without <c>TicketCompanyDefaults.MaxDayTicketInactivity</c>
    /// cannot have inactive tickets — the calculator returns <c>IsInactive = false</c>
    /// whenever the threshold is null.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyHasNoInactivityThreshold_ShouldReportZeroInactive()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new TestContext(companyId);
        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = Guid.NewGuid(),
                    ["Created"] = DateTime.UtcNow.AddYears(-1),
                    ["AssignedToUserId"] = Guid.NewGuid(),
                    ["ResolutionTimeUnits"] = 1,
                    ["ComplexityTimeUnitCode"] = 24,
                    ["MaxDayTicketInactivity"] = null,
                    ["LastActivityAt"] = DateTime.UtcNow.AddYears(-1)
                }));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetTicketSummaryQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.InactiveCount.Should().Be(0);
        response.Data.OpenCount.Should().Be(1);
    }

    /// <summary>
    /// Empty result set → all four counts are zero, the snapshot is still
    /// stamped with the current UTC instant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoOpenTickets_ShouldReturnAllZeros()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new TestContext(companyId);
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id", "Created", "AssignedToUserId",
                "ResolutionTimeUnits", "ComplexityTimeUnitCode",
                "MaxDayTicketInactivity", "LastActivityAt"));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetTicketSummaryQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.OpenCount.Should().Be(0);
        response.Data.SlaBreachedCount.Should().Be(0);
        response.Data.InactiveCount.Should().Be(0);
        response.Data.UnassignedCount.Should().Be(0);
    }

    /// <summary>
    /// SPEC 52: the summary takes no company parameter; a SuperAdmin also gets
    /// the active company of the token, never another one or the global view.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSuperAdmin_ShouldUseTokenCompany()
    {
        var tokenCompanyId = _fixture.Create<Guid>();
        var context = new TestContext(tokenCompanyId, isSuperAdmin: true);
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id", "Created", "AssignedToUserId",
                "ResolutionTimeUnits", "ComplexityTimeUnitCode",
                "MaxDayTicketInactivity", "LastActivityAt"));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetTicketSummaryQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Connection.CapturedParameters["TenantId"].Should().Be(tokenCompanyId);
    }

    /// <summary>
    /// Holds the reusable mocks.
    /// </summary>
    private sealed class TestContext
    {
        public TestContext(Guid companyId, bool isSuperAdmin = false)
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);
            CurrentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(isSuperAdmin);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetTicketSummaryQueryHandler CreateHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
    }
}