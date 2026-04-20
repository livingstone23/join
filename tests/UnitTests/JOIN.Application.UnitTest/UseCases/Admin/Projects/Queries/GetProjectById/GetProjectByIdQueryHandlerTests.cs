using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.Projects.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Projects.Queries.GetProjectById;

/// <summary>
/// Contains the unit tests for the project detail query handler.
/// These tests verify tenant protection, not-found behavior, and the successful detail path.
/// </summary>
public sealed class GetProjectByIdQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant identifier is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        // Arrange
        var context = new GetProjectByIdQueryHandlerTestContext();
        var query = new GetProjectByIdQuery(_fixture.Create<Guid>(), Guid.Empty);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        response.Errors.Should().Contain("The X-Company-Id header is required.");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path when the project exists for the current tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenProjectExists_ShouldReturnProjectDetails()
    {
        // Arrange
        var projectId = _fixture.Create<Guid>();
        var companyId = _fixture.Create<Guid>();
        var entityStatusId = _fixture.Create<Guid>();
        var createdAt = new DateTime(2026, 4, 18, 8, 45, 0, DateTimeKind.Utc);

        var context = new GetProjectByIdQueryHandlerTestContext();
        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = projectId,
                    ["CompanyId"] = companyId,
                    ["CompanyName"] = "JOIN CRM",
                    ["Name"] = "Portal Migration",
                    ["EntityStatusId"] = entityStatusId,
                    ["EntityStatusName"] = "Active",
                    ["CreatedAt"] = createdAt
                }));

        var query = new GetProjectByIdQuery(projectId, companyId);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Project retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(projectId);
        response.Data.CompanyId.Should().Be(companyId);
        response.Data.CompanyName.Should().Be("JOIN CRM");
        response.Data.Name.Should().Be("Portal Migration");
        response.Data.EntityStatusId.Should().Be(entityStatusId);
        response.Data.EntityStatusName.Should().Be("Active");
        response.Data.CreatedAt.Should().Be(createdAt);

        context.Connection.LastCommandText.Should().Contain("WHERE p.Id = @Id");
        context.Connection.LastCommandText.Should().Contain("AND p.CompanyId = @CompanyId");
        context.Connection.CapturedParameters["Id"].Should().Be(projectId);
        context.Connection.CapturedParameters["CompanyId"].Should().Be(companyId);
    }

    /// <summary>
    /// Verifies the not-found branch when the current tenant has no matching project.
    /// </summary>
    [Fact]
    public async Task Handle_WhenProjectDoesNotExist_ShouldReturnProjectNotFoundError()
    {
        // Arrange
        var context = new GetProjectByIdQueryHandlerTestContext();
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "CompanyId",
                "CompanyName",
                "Name",
                "EntityStatusId",
                "EntityStatusName",
                "CreatedAt"));

        var query = new GetProjectByIdQuery(_fixture.Create<Guid>(), _fixture.Create<Guid>());
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PROJECT_NOT_FOUND");
        response.Errors.Should().Contain("Project not found.");
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the project detail tests.
    /// </summary>
    private sealed class GetProjectByIdQueryHandlerTestContext
    {
        public GetProjectByIdQueryHandlerTestContext()
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetProjectByIdQueryHandler CreateHandler()
        {
            return new GetProjectByIdQueryHandler(ConnectionFactoryMock.Object);
        }
    }
}
