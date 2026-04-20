using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Common.StreetTypes.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.StreetTypes.Queries.GetStreetTypeById;

/// <summary>
/// Contains the unit tests for the street type detail query handler.
/// These tests verify the successful detail path and the not-found behavior.
/// </summary>
public sealed class GetStreetTypeByIdQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the happy path when the street type exists.
    /// </summary>
    [Fact]
    public async Task Handle_WhenStreetTypeExists_ShouldReturnStreetTypeDetails()
    {
        // Arrange
        var streetTypeId = _fixture.Create<Guid>();
        var context = new GetStreetTypeByIdQueryHandlerTestContext();

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = streetTypeId,
                    ["Name"] = "Avenue",
                    ["Abbreviation"] = "Ave",
                    ["IsActive"] = true
                }));

        var query = new GetStreetTypeByIdQuery(streetTypeId);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Street type retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(streetTypeId);
        response.Data.Name.Should().Be("Avenue");
        response.Data.Abbreviation.Should().Be("Ave");
        response.Data.IsActive.Should().BeTrue();

        context.Connection.LastCommandText.Should().Contain("WHERE st.Id = @Id");
        context.Connection.LastCommandText.Should().Contain("AND st.GcRecord = 0");
        context.Connection.CapturedParameters["Id"].Should().Be(streetTypeId);
    }

    /// <summary>
    /// Verifies the not-found branch when no active street type matches the request.
    /// </summary>
    [Fact]
    public async Task Handle_WhenStreetTypeDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var context = new GetStreetTypeByIdQueryHandlerTestContext();
        context.Connection.SetResults(
            FakeResultSet.Empty("Id", "Name", "Abbreviation", "IsActive"));

        var query = new GetStreetTypeByIdQuery(_fixture.Create<Guid>());
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("STREETTYPE_NOT_FOUND");
        response.Errors.Should().Contain("Street type not found.");
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the street type detail tests.
    /// </summary>
    private sealed class GetStreetTypeByIdQueryHandlerTestContext
    {
        public GetStreetTypeByIdQueryHandlerTestContext()
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetStreetTypeByIdQueryHandler CreateHandler()
        {
            return new GetStreetTypeByIdQueryHandler(ConnectionFactoryMock.Object);
        }
    }
}
