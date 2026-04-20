using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketComplexities.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketComplexities.Queries.GetTicketComplexityById;

/// <summary>
/// Contains the unit tests for the single ticket complexity detail query.
/// These tests validate the tenant guard, the happy path, and the not-found branch.
/// </summary>
public sealed class GetTicketComplexityByIdQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the CompanyId claim is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = new GetTicketComplexityByIdQueryTestContext(Guid.Empty);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetTicketComplexityByIdQuery(_fixture.Create<Guid>()), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        response.Errors.Should().Contain("The authenticated token must contain a valid CompanyId claim.");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path when the ticket complexity exists for the current tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEntityExists_ShouldReturnTicketComplexityDetails()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var entityId = _fixture.Create<Guid>();
        var timeUnitId = _fixture.Create<Guid>();
        var context = new GetTicketComplexityByIdQueryTestContext(companyId);

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = entityId,
                    ["CompanyId"] = companyId,
                    ["CompanyName"] = "JOIN CRM",
                    ["Name"] = "Standard",
                    ["Description"] = "Standard complexity",
                    ["Code"] = 10,
                    ["ResolutionTimeUnits"] = 2,
                    ["TimeUnitId"] = timeUnitId,
                    ["IsActive"] = true,
                    ["CreatedAt"] = DateTime.UtcNow
                }));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetTicketComplexityByIdQuery(entityId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket complexity retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(entityId);
        response.Data.CompanyId.Should().Be(companyId);
        response.Data.Name.Should().Be("Standard");
        response.Data.TimeUnitId.Should().Be(timeUnitId);

        context.Connection.LastCommandText.Should().Contain("WHERE tc.Id = @Id");
        context.Connection.LastCommandText.Should().Contain("AND tc.CompanyId = @TenantId");
        context.Connection.LastCommandText.Should().Contain("AND tc.GcRecord = 0");
        context.Connection.CapturedParameters["Id"].Should().Be(entityId);
        context.Connection.CapturedParameters["TenantId"].Should().Be(companyId);
    }

    /// <summary>
    /// Verifies the not-found response when no record matches the requested identifier.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEntityDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var entityId = _fixture.Create<Guid>();
        var context = new GetTicketComplexityByIdQueryTestContext(companyId);
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "CompanyId",
                "CompanyName",
                "Name",
                "Description",
                "Code",
                "ResolutionTimeUnits",
                "TimeUnitId",
                "IsActive",
                "CreatedAt"));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetTicketComplexityByIdQuery(entityId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_COMPLEXITY_NOT_FOUND");
        response.Errors.Should().Contain("Ticket complexity not found.");
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the detail query tests.
    /// </summary>
    private sealed class GetTicketComplexityByIdQueryTestContext
    {
        public GetTicketComplexityByIdQueryTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetTicketComplexityByIdQueryHandler CreateHandler()
        {
            return new GetTicketComplexityByIdQueryHandler(
                ConnectionFactoryMock.Object,
                CurrentUserServiceMock.Object);
        }
    }
}
