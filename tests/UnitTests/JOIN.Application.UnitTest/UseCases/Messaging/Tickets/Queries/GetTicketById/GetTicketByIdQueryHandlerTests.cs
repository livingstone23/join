using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.Tickets.Queries;
using JOIN.Domain.Enums;
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
                "PersonId",
                "PersonName",
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
    /// Verifies the early exit when the UserId claim is missing or invalid.
    /// Added in SPEC 35 F9 — the log-visibility filter requires a parseable
    /// viewer id, so without one the handler must refuse the read entirely.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserIdIsInvalid_ShouldReturnUserRequiredError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new GetTicketByIdQueryHandlerTestContext(companyId);
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns("not-a-guid");

        var query = new GetTicketByIdQuery(_fixture.Create<Guid>());
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("USER_REQUIRED");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Verifies that the SQL exposes the public branch of the visibility filter
    /// (SPEC 35 F9). A row with <c>IsOnlyForCreatedAndAssigned = 0</c> must be
    /// returned to any viewer with read access to the ticket — this branch is
    /// the only one that does not depend on identity.
    /// </summary>
    [Fact]
    public async Task Handle_WhenLogIsPublic_ShouldExposeIsOnlyForCreatedAndAssignedBranch()
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
        await handler.Handle(query, CancellationToken.None);

        // Assert
        context.Connection.LastCommandText.Should().Contain("tl.IsOnlyForCreatedAndAssigned = 0");
    }

    /// <summary>
    /// Verifies that the SQL grants the ticket's creator access to private
    /// (IsOnlyForCreatedAndAssigned = 1) log rows by comparing
    /// <c>t.CreatedByUserId</c> against the viewer parameter.
    /// </summary>
    [Fact]
    public async Task Handle_WhenViewerIsCreator_ShouldExposeCreatorBranch()
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
        await handler.Handle(query, CancellationToken.None);

        // Assert
        context.Connection.LastCommandText.Should().Contain("t.CreatedByUserId = @ViewerId");
    }

    /// <summary>
    /// Verifies that the SQL grants the current assignee access to private
    /// log rows by comparing <c>t.AssignedToUserId</c> against the viewer parameter.
    /// </summary>
    [Fact]
    public async Task Handle_WhenViewerIsAssignee_ShouldExposeAssigneeBranch()
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
        await handler.Handle(query, CancellationToken.None);

        // Assert
        context.Connection.LastCommandText.Should().Contain("t.AssignedToUserId = @ViewerId");
    }

    /// <summary>
    /// Verifies that the SQL grants ticket super-admins (a tenant
    /// <c>IsSuperAdminTicket = 1</c> flag in <c>Messaging.TicketUserCompanies</c>)
    /// access to private log rows — even when they are neither the creator nor
    /// the current assignee.
    /// </summary>
    [Fact]
    public async Task Handle_WhenViewerIsSuperAdmin_ShouldExposeExistsBranch()
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
        await handler.Handle(query, CancellationToken.None);

        // Assert
        context.Connection.LastCommandText.Should().Contain("Messaging.TicketUserCompanies");
        context.Connection.LastCommandText.Should().Contain("tuc.IsSuperAdminTicket = 1");
    }

    /// <summary>
    /// Verifies that all four visibility branches coexist, OR-combined inside
    /// the WHERE clause. A third viewer — not the creator, not the assignee,
    /// and not a super-admin — only sees rows where
    /// <c>IsOnlyForCreatedAndAssigned = 0</c>; every other branch evaluates
    /// false for them, so private entries stay hidden.
    /// </summary>
    [Fact]
    public async Task Handle_WhenViewerHasNoPrivilege_ShouldCombineAllBranchesWithOr()
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
        await handler.Handle(query, CancellationToken.None);

        // Assert
        var sql = context.Connection.LastCommandText;
        var orBranches = new[]
        {
            "tl.IsOnlyForCreatedAndAssigned = 0",
            "t.CreatedByUserId = @ViewerId",
            "t.AssignedToUserId = @ViewerId",
            "EXISTS"
        };

        foreach (var branch in orBranches)
        {
            sql.Should().Contain(branch, $"the WHERE clause must expose branch '{branch}'");
        }
    }

    /// <summary>
    /// Verifies that the <c>CASE tl.LogType</c> expression maps the new
    /// <see cref="LogType.Finalization"/> enum value (5) to the string
    /// 'Finalization' (SPEC 35 F1/F9).
    /// </summary>
    [Fact]
    public async Task Handle_WhenRenderingLogType_ShouldMapValue5ToFinalization()
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
        await handler.Handle(query, CancellationToken.None);

        // Assert
        context.Connection.LastCommandText.Should().Contain("WHEN 5 THEN 'Finalization'");
    }

    /// <summary>
    /// Verifies that the handler resolves @ViewerId from the current user and
    /// forwards it to the connection — without this, the privacy filter would
    /// compare against a null/zero GUID and silently expose every private log
    /// row to anyone who can read the ticket.
    /// </summary>
    [Fact]
    public async Task Handle_WhenExecutingSql_ShouldPassViewerIdParameter()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var viewerId = _fixture.Create<Guid>();
        var ticketId = _fixture.Create<Guid>();
        var context = new GetTicketByIdQueryHandlerTestContext(companyId, viewerId);

        context.Connection.SetResults(
            CreateTicketDetailResultSet(ticketId, companyId),
            CreateTicketLogsResultSet());

        var query = new GetTicketByIdQuery(ticketId);
        var handler = context.CreateHandler();

        // Act
        await handler.Handle(query, CancellationToken.None);

        // Assert
        context.Connection.CapturedParameters.Should().ContainKey("ViewerId");
        context.Connection.CapturedParameters["ViewerId"].Should().Be(viewerId);
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
                ["PersonId"] = Guid.NewGuid(),
                ["PersonName"] = "Contoso",
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
        public GetTicketByIdQueryHandlerTestContext(Guid companyId, Guid? viewerId = null)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns((viewerId ?? Guid.NewGuid()).ToString());
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
