using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.IdentificationTypes.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.IdentificationTypes.Queries.GetIdentificationTypeById;

/// <summary>
/// Contains the unit tests for the identification type detail query handler.
/// These tests verify the successful detail path and the not-found behavior.
/// </summary>
public sealed class GetIdentificationTypeByIdQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the happy path when the identification type exists.
    /// </summary>
    [Fact]
    public async Task Handle_WhenIdentificationTypeExists_ShouldReturnIdentificationTypeDetails()
    {
        // Arrange
        var identificationTypeId = _fixture.Create<Guid>();
        var createdAt = new DateTime(2026, 4, 18, 10, 0, 0, DateTimeKind.Utc);
        var context = new GetIdentificationTypeByIdQueryHandlerTestContext();

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = identificationTypeId,
                    ["Name"] = "Passport",
                    ["Description"] = "International document",
                    ["ValidationPattern"] = "^[A-Z0-9]{6,20}$",
                    ["IsActive"] = true,
                    ["CreatedAt"] = createdAt
                }));

        var query = new GetIdentificationTypeByIdQuery(identificationTypeId);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Identification type retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(identificationTypeId);
        response.Data.Name.Should().Be("Passport");
        response.Data.Description.Should().Be("International document");
        response.Data.ValidationPattern.Should().Be("^[A-Z0-9]{6,20}$");
        response.Data.IsActive.Should().BeTrue();
        response.Data.CreatedAt.Should().Be(createdAt);

        context.Connection.LastCommandText.Should().Contain("WHERE it.Id = @Id");
        context.Connection.LastCommandText.Should().Contain("AND it.GcRecord = 0");
        context.Connection.CapturedParameters["Id"].Should().Be(identificationTypeId);
    }

    /// <summary>
    /// Verifies the not-found branch when no active identification type matches the request.
    /// </summary>
    [Fact]
    public async Task Handle_WhenIdentificationTypeDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var context = new GetIdentificationTypeByIdQueryHandlerTestContext();
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "Name",
                "Description",
                "ValidationPattern",
                "IsActive",
                "CreatedAt"));

        var query = new GetIdentificationTypeByIdQuery(_fixture.Create<Guid>());
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("IDENTIFICATION_TYPE_NOT_FOUND");
        response.Errors.Should().Contain("Identification type not found.");
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the identification type detail tests.
    /// </summary>
    private sealed class GetIdentificationTypeByIdQueryHandlerTestContext
    {
        public GetIdentificationTypeByIdQueryHandlerTestContext()
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetIdentificationTypeByIdQueryHandler CreateHandler()
        {
            return new GetIdentificationTypeByIdQueryHandler(ConnectionFactoryMock.Object);
        }
    }
}
