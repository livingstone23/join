using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.Tickets.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.GetTicketById;

/// <summary>
/// Contains the unit tests for the single-ticket detail query.
/// These tests verify tenant protection, successful detail retrieval,
/// and the not-found branch returned by the handler.
/// </summary>
public sealed class GetTicketByIdQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant identifier is missing.
    /// This prevents the handler from reading ticket data without a valid company context.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = new GetTicketByIdQueryHandlerTestContext(Guid.Empty);
        var query = new GetTicketByIdQuery(_fixture.Create<Guid>());
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path for retrieving one ticket and its audit logs.
    /// This test ensures the SQL remains tenant-scoped and that the handler populates the log collection.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTicketExists_ShouldReturnTicketDetailsWithLogs()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var ticketId = _fixture.Create<Guid>();
        var context = new GetTicketByIdQueryHandlerTestContext(companyId);

        context.Connection.SetResults(
            CreateTicketDetailResultSet(ticketId, companyId),
            CreateTicketLogsResultSet());

        var query = new GetTicketByIdQuery(ticketId);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(ticketId);
        response.Data.CompanyId.Should().Be(companyId);
        response.Data.CompanyName.Should().Be("JOIN");
        response.Data.Code.Should().Be("TICK-202604-1001");
        response.Data.Logs.Should().HaveCount(1);
        response.Data.Logs.Single().LogType.Should().Be("Creation");
        response.Data.Logs.Single().Summary.Should().Be("Ticket created from portal");

        context.Connection.LastCommandText.Should().Contain("WHERE t.Id = @Id");
        context.Connection.LastCommandText.Should().Contain("AND t.CompanyId = @TenantId");
        context.Connection.LastCommandText.Should().Contain("AND t.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("WHERE tl.TicketId = @Id");
        context.Connection.LastCommandText.Should().Contain("AND tl.CompanyId = @TenantId");

        context.Connection.CapturedParameters["Id"].Should().Be(ticketId);
        context.Connection.CapturedParameters["TenantId"].Should().Be(companyId);
    }

    /// <summary>
    /// Verifies the not-found branch when the query returns no ticket for the current tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTicketDoesNotExist_ShouldReturnTicketNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var ticketId = _fixture.Create<Guid>();
        var context = new GetTicketByIdQueryHandlerTestContext(companyId);

        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "CompanyId",
                "CompanyName",
                "Code",
                "Name",
                "Description",
                "EstimatedTime",
                "ConsumedTime",
                "IsVisibleToExternals",
                "TicketStatusId",
                "TicketStatusName",
                "TicketComplexityId",
                "TicketComplexityName",
                "TimeUnitId",
                "TimeUnitName",
                "CustomerId",
                "CustomerName",
                "ProjectId",
                "ProjectName",
                "AreaId",
                "AreaName",
                "ChannelId",
                "ChannelName",
                "CreatedByUserId",
                "CreatedByUserName",
                "AssignedToUserId",
                "AssignedToUserName",
                "PrecedentTicketId",
                "PrecedentTicketCode",
                "CreatedAt"),
            FakeResultSet.Empty(
                "Id",
                "LogType",
                "Summary",
                "CreatedAt",
                "UserRegisteredName",
                "PreviousStatusName",
                "NewStatusName",
                "ConsumedTime"));

        var query = new GetTicketByIdQuery(ticketId);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_NOT_FOUND");
        response.Data.Should().BeNull();
    }

    /// <summary>
    /// Creates a fake result set containing one flattened ticket detail row.
    /// </summary>
    private static FakeResultSet CreateTicketDetailResultSet(Guid ticketId, Guid companyId)
    {
        return FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = ticketId,
                ["CompanyId"] = companyId,
                ["CompanyName"] = "JOIN",
                ["Code"] = "TICK-202604-1001",
                ["Name"] = "Portal issue",
                ["Description"] = "A sample ticket for detail retrieval.",
                ["EstimatedTime"] = 8m,
                ["ConsumedTime"] = 2m,
                ["IsVisibleToExternals"] = true,
                ["TicketStatusId"] = Guid.NewGuid(),
                ["TicketStatusName"] = "Open",
                ["TicketComplexityId"] = Guid.NewGuid(),
                ["TicketComplexityName"] = "High",
                ["TimeUnitId"] = Guid.NewGuid(),
                ["TimeUnitName"] = "Hours",
                ["CustomerId"] = Guid.NewGuid(),
                ["CustomerName"] = "Contoso",
                ["ProjectId"] = Guid.NewGuid(),
                ["ProjectName"] = "CRM Rollout",
                ["AreaId"] = Guid.NewGuid(),
                ["AreaName"] = "Support",
                ["ChannelId"] = Guid.NewGuid(),
                ["ChannelName"] = "Portal Web",
                ["CreatedByUserId"] = Guid.NewGuid(),
                ["CreatedByUserName"] = "Ana Torres",
                ["AssignedToUserId"] = Guid.NewGuid(),
                ["AssignedToUserName"] = "Luis Gomez",
                ["PrecedentTicketId"] = Guid.NewGuid(),
                ["PrecedentTicketCode"] = "TICK-202604-0999",
                ["CreatedAt"] = DateTime.UtcNow
            });
    }

    /// <summary>
    /// Creates a fake result set containing one audit log row.
    /// </summary>
    private static FakeResultSet CreateTicketLogsResultSet()
    {
        return FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["LogType"] = "Creation",
                ["Summary"] = "Ticket created from portal",
                ["CreatedAt"] = DateTime.UtcNow,
                ["UserRegisteredName"] = "Ana Torres",
                ["PreviousStatusName"] = null,
                ["NewStatusName"] = "Open",
                ["ConsumedTime"] = 0m
            });
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the detail query tests.
    /// </summary>
    private sealed class GetTicketByIdQueryHandlerTestContext
    {
        public GetTicketByIdQueryHandlerTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetTicketByIdQueryHandler CreateHandler()
        {
            return new GetTicketByIdQueryHandler(
                ConnectionFactoryMock.Object,
                CurrentUserServiceMock.Object);
        }
    }
}
