using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Common.Regions.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Regions.Queries.GetRegionById;

/// <summary>
/// Contains the unit tests for the region detail query.
/// These tests verify the happy path and the not-found response branch.
/// </summary>
public sealed class GetRegionByIdQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies that an existing region is returned successfully.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRegionExists_ShouldReturnRegionDto()
    {
        // Arrange
        var regionId = _fixture.Create<Guid>();
        var countryId = _fixture.Create<Guid>();
        var context = new GetRegionByIdQueryTestContext();

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = regionId,
                    ["Name"] = "North",
                    ["Code"] = "CA",
                    ["CountryId"] = countryId,
                    ["CountryName"] = "Canada",
                    ["CreatedAt"] = DateTime.UtcNow
                }));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetRegionByIdQuery(regionId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Region retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(regionId);
        response.Data.CountryId.Should().Be(countryId);
        response.Data.CountryName.Should().Be("Canada");
        context.Connection.LastCommandText.Should().Contain("WHERE r.Id = @Id");
        context.Connection.LastCommandText.Should().Contain("AND r.GcRecord = 0");
        context.Connection.CapturedParameters["Id"].Should().Be(regionId);
    }

    /// <summary>
    /// Verifies that a missing region returns the not-found response.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRegionDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var regionId = _fixture.Create<Guid>();
        var context = new GetRegionByIdQueryTestContext();
        context.Connection.SetResults(FakeResultSet.Empty("Id", "Name", "Code", "CountryId", "CountryName", "CreatedAt"));
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetRegionByIdQuery(regionId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("REGION_NOT_FOUND");
        response.Errors.Should().Contain("Region not found.");
    }

    /// <summary>
    /// SPEC 38: short-circuit with COMPANY_REQUIRED when the token carries no CompanyId.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTokenHasNoCompanyId_ShouldReturnCompanyRequiredError()
    {
        var context = new GetRegionByIdQueryTestContext();
        var handler = context.CreateHandler();
        // SPEC 38: setup AFTER CreateHandler because Moq's last-wins semantics would otherwise
        // overwrite this Guid.Empty setup with the handler-context default.
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var response = await handler.Handle(new GetRegionByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        response.Errors.Should().Contain("The authenticated token must contain a valid CompanyId claim.");

        context.Connection.LastCommandText.Should().BeNullOrWhiteSpace(
            "no SQL should reach the connection when the token has no CompanyId claim");
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the detail query tests.
    /// </summary>
    private sealed class GetRegionByIdQueryTestContext
    {
        public GetRegionByIdQueryTestContext()
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public GetRegionByIdQueryHandler CreateHandler()
        {
            // SPEC 38: tests supply a stable tenant id so r.CompanyId = @TenantId matches.
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
            return new GetRegionByIdQueryHandler(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
        }
    }
}
