using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Common.Municipalities.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Municipalities.Queries.GetMunicipalityById;

/// <summary>
/// Contains the unit tests for the municipality detail query.
/// These tests verify the happy path and the not-found response branch.
/// </summary>
public sealed class GetMunicipalityByIdQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies that an existing municipality is returned successfully.
    /// </summary>
    [Fact]
    public async Task Handle_WhenMunicipalityExists_ShouldReturnMunicipalityDto()
    {
        // Arrange
        var municipalityId = _fixture.Create<Guid>();
        var provinceId = _fixture.Create<Guid>();
        var context = new GetMunicipalityByIdQueryTestContext();

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = municipalityId,
                    ["Name"] = "Managua",
                    ["Code"] = "MN",
                    ["ProvinceId"] = provinceId,
                    ["ProvinceName"] = "Managua Province",
                    ["CreatedAt"] = DateTime.UtcNow
                }));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetMunicipalityByIdQuery(municipalityId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Municipality retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(municipalityId);
        response.Data.ProvinceId.Should().Be(provinceId);
        response.Data.ProvinceName.Should().Be("Managua Province");
        context.Connection.LastCommandText.Should().Contain("INNER JOIN Common.Provinces p");
        context.Connection.LastCommandText.Should().Contain("WHERE m.Id = @Id");
        context.Connection.LastCommandText.Should().Contain("AND m.GcRecord = 0");
    }

    /// <summary>
    /// Verifies that a missing municipality returns the not-found response.
    /// </summary>
    [Fact]
    public async Task Handle_WhenMunicipalityDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var municipalityId = _fixture.Create<Guid>();
        var context = new GetMunicipalityByIdQueryTestContext();
        context.Connection.SetResults(FakeResultSet.Empty("Id", "Name", "Code", "ProvinceId", "ProvinceName", "CreatedAt"));
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetMunicipalityByIdQuery(municipalityId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("MUNICIPALITY_NOT_FOUND");
        response.Errors.Should().Contain("Municipality not found.");
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the detail query tests.
    /// </summary>
    private sealed class GetMunicipalityByIdQueryTestContext
    {
        public GetMunicipalityByIdQueryTestContext()
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetMunicipalityByIdQueryHandler CreateHandler()
        {
            return new GetMunicipalityByIdQueryHandler(ConnectionFactoryMock.Object);
        }
    }
}
