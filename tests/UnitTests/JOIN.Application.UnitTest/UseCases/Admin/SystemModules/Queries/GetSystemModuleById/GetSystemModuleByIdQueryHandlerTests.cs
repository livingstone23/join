using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.SystemModules.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.SystemModules.Queries.GetSystemModuleById;

/// <summary>
/// Contains the unit tests for the single system module detail query.
/// These tests validate the happy path and the not-found response branch.
/// </summary>
public sealed class GetSystemModuleByIdQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the happy path when the module exists.
    /// </summary>
    [Fact]
    public async Task Handle_WhenModuleExists_ShouldReturnSystemModuleDetails()
    {
        // Arrange
        var moduleId = _fixture.Create<Guid>();
        var context = new GetSystemModuleByIdQueryTestContext();

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = moduleId,
                    ["Name"] = "CRM",
                    ["Description"] = "Customer management",
                    ["Icon"] = "fa-users",
                    ["IsActive"] = true,
                    ["CreatedAt"] = DateTime.UtcNow
                }));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetSystemModuleByIdQuery(moduleId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("System module retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(moduleId);
        response.Data.Name.Should().Be("CRM");

        context.Connection.LastCommandText.Should().Contain("WHERE sm.Id = @Id");
        context.Connection.LastCommandText.Should().Contain("AND sm.GcRecord = 0");
        context.Connection.CapturedParameters["Id"].Should().Be(moduleId);
    }

    /// <summary>
    /// Verifies the not-found response when the requested module does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenModuleDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var moduleId = _fixture.Create<Guid>();
        var context = new GetSystemModuleByIdQueryTestContext();

        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "Name",
                "Description",
                "Icon",
                "IsActive",
                "CreatedAt"));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetSystemModuleByIdQuery(moduleId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("SYSTEM_MODULE_NOT_FOUND");
        response.Errors.Should().Contain("System module not found.");
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the detail query tests.
    /// </summary>
    private sealed class GetSystemModuleByIdQueryTestContext
    {
        public GetSystemModuleByIdQueryTestContext()
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetSystemModuleByIdQueryHandler CreateHandler()
        {
            return new GetSystemModuleByIdQueryHandler(ConnectionFactoryMock.Object);
        }
    }
}
