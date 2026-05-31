using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Common.Provinces.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Provinces.Queries.GetProvinceById;

/// <summary>
/// Contains the unit tests for the province detail query.
/// These tests verify the happy path, optional region projection, and not-found behavior.
/// </summary>
public sealed class GetProvinceByIdQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies that an existing province is returned successfully.
    /// </summary>
    [Fact]
    public async Task Handle_WhenProvinceExists_ShouldReturnProvinceDto()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var countryId = _fixture.Create<Guid>();
        var regionId = _fixture.Create<Guid>();
        var context = new GetProvinceByIdQueryTestContext();

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = provinceId,
                    ["Name"] = "Managua",
                    ["Code"] = "MN",
                    ["CountryId"] = countryId,
                    ["CountryName"] = "Nicaragua",
                    ["RegionId"] = regionId,
                    ["RegionName"] = "Pacific",
                    ["CreatedAt"] = DateTime.UtcNow
                }));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetProvinceByIdQuery(provinceId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Province retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(provinceId);
        response.Data.RegionId.Should().Be(regionId);
        response.Data.RegionName.Should().Be("Pacific");
        context.Connection.LastCommandText.Should().Contain("LEFT JOIN Admin.Regions r");
        context.Connection.LastCommandText.Should().Contain("WHERE p.Id = @Id");
        context.Connection.CapturedParameters["Id"].Should().Be(provinceId);
    }

    /// <summary>
    /// Verifies that provinces without a linked region still return successfully.
    /// </summary>
    [Fact]
    public async Task Handle_WhenProvinceHasNoRegion_ShouldReturnDtoWithNullRegion()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var context = new GetProvinceByIdQueryTestContext();

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = provinceId,
                    ["Name"] = "León",
                    ["Code"] = "LE",
                    ["CountryId"] = _fixture.Create<Guid>(),
                    ["CountryName"] = "Nicaragua",
                    ["RegionId"] = null,
                    ["RegionName"] = null,
                    ["CreatedAt"] = DateTime.UtcNow
                }));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetProvinceByIdQuery(provinceId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.RegionId.Should().BeNull();
        response.Data.RegionName.Should().BeNull();
    }

    /// <summary>
    /// Verifies that a missing province returns the not-found response.
    /// </summary>
    [Fact]
    public async Task Handle_WhenProvinceDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var context = new GetProvinceByIdQueryTestContext();
        context.Connection.SetResults(FakeResultSet.Empty("Id", "Name", "Code", "CountryId", "CountryName", "RegionId", "RegionName", "CreatedAt"));
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetProvinceByIdQuery(provinceId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PROVINCE_NOT_FOUND");
        response.Errors.Should().Contain("Province not found.");
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the detail query tests.
    /// </summary>
    private sealed class GetProvinceByIdQueryTestContext
    {
        public GetProvinceByIdQueryTestContext()
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetProvinceByIdQueryHandler CreateHandler()
        {
            return new GetProvinceByIdQueryHandler(ConnectionFactoryMock.Object);
        }
    }
}
