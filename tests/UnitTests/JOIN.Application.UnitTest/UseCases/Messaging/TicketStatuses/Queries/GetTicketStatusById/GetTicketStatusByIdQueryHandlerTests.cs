using AutoFixture;
using FluentAssertions;
using JOIN.Application.Exceptions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketStatuses.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketStatuses.Queries.GetTicketStatusById;

/// <summary>
/// Contains the unit tests for the single ticket status detail query.
/// These tests validate the tenant guard, the happy path, and the not-found branch.
/// </summary>
public sealed class GetTicketStatusByIdQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the CompanyId claim is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = new GetTicketStatusByIdQueryTestContext(Guid.Empty);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetTicketStatusByIdQuery(_fixture.Create<Guid>()), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        response.Errors.Should().Contain("The authenticated token must contain a valid CompanyId claim.");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path when the ticket status exists for the current tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenStatusExists_ShouldReturnTicketStatusDetails()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var context = new GetTicketStatusByIdQueryTestContext(companyId);

        context.Connection.SetResults(CreateDetailResultSet(statusId, companyId));
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetTicketStatusByIdQuery(statusId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket status retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(statusId);
        response.Data.CompanyId.Should().Be(companyId);
        response.Data.CompanyName.Should().Be("JOIN CRM");
        response.Data.Name.Should().Be("Open");
        response.Data.Code.Should().Be(10);

        context.Connection.LastCommandText.Should().Contain("WHERE ts.Id = @Id");
        context.Connection.LastCommandText.Should().Contain("AND ts.CompanyId = @TenantId");
        context.Connection.LastCommandText.Should().Contain("AND ts.GcRecord = 0");
        context.Connection.CapturedParameters["Id"].Should().Be(statusId);
        context.Connection.CapturedParameters["TenantId"].Should().Be(companyId);
    }

    /// <summary>
    /// Verifies that the handler throws a not-found exception when the status does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenStatusDoesNotExist_ShouldThrowNotFoundException()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var context = new GetTicketStatusByIdQueryTestContext(companyId);
        context.Connection.SetResults(FakeResultSet.Empty(
            "Id",
            "CompanyId",
            "CompanyName",
            "Name",
            "Description",
            "Code",
            "IsActive",
            "IsInitial",
            "IsPaused",
            "IsFinal",
            "CreatedAt"));

        var handler = context.CreateHandler();

        // Act
        var action = async () => await handler.Handle(new GetTicketStatusByIdQuery(statusId), CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<NotFoundException>();
    }

    /// <summary>
    /// Creates a fake result set containing one ticket status detail row.
    /// </summary>
    private static FakeResultSet CreateDetailResultSet(Guid statusId, Guid companyId)
    {
        return FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = statusId,
                ["CompanyId"] = companyId,
                ["CompanyName"] = "JOIN CRM",
                ["Name"] = "Open",
                ["Description"] = "Initial workflow status",
                ["Code"] = 10,
                ["IsActive"] = true,
                ["IsInitial"] = true,
                ["IsPaused"] = false,
                ["IsFinal"] = false,
                ["CreatedAt"] = DateTime.UtcNow
            });
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the detail query tests.
    /// </summary>
    private sealed class GetTicketStatusByIdQueryTestContext
    {
        public GetTicketStatusByIdQueryTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetTicketStatusByIdQueryHandler CreateHandler()
        {
            return new GetTicketStatusByIdQueryHandler(
                ConnectionFactoryMock.Object,
                CurrentUserServiceMock.Object);
        }
    }
}
