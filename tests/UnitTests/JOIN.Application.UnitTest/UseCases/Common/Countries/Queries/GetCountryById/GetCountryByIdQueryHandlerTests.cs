using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Common.Countries.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Countries.Queries.GetCountryById;

/// <summary>
/// Contains the unit tests for the country detail query handler.
/// These tests verify the successful detail path and the not-found behavior.
/// </summary>
public sealed class GetCountryByIdQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the happy path when the country exists.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCountryExists_ShouldReturnCountryDetails()
    {
        // Arrange
        var countryId = _fixture.Create<Guid>();
        var context = new GetCountryByIdQueryHandlerTestContext();

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = countryId,
                    ["Name"] = "Panama",
                    ["IsoCode"] = "PA"
                }));

        var query = new GetCountryByIdQuery(countryId);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Country retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(countryId);
        response.Data.Name.Should().Be("Panama");
        response.Data.IsoCode.Should().Be("PA");

        context.Connection.LastCommandText.Should().Contain("WHERE c.Id = @Id");
        context.Connection.LastCommandText.Should().Contain("AND c.GcRecord = 0");
        context.Connection.CapturedParameters["Id"].Should().Be(countryId);
    }

    /// <summary>
    /// Verifies the not-found branch when no active country matches the request.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCountryDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var context = new GetCountryByIdQueryHandlerTestContext();
        context.Connection.SetResults(
            FakeResultSet.Empty("Id", "Name", "IsoCode"));

        var query = new GetCountryByIdQuery(_fixture.Create<Guid>());
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COUNTRY_NOT_FOUND");
        response.Errors.Should().Contain("Country not found.");
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the country detail tests.
    /// </summary>
    private sealed class GetCountryByIdQueryHandlerTestContext
    {
        public GetCountryByIdQueryHandlerTestContext()
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetCountryByIdQueryHandler CreateHandler()
        {
            return new GetCountryByIdQueryHandler(ConnectionFactoryMock.Object);
        }
    }
}
