using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Common.Provinces.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Provinces.Queries.GetProvinces;

/// <summary>
/// Contains the unit tests for the province listing query.
/// These tests verify filtering, pagination sanitization, and empty-result behavior.
/// </summary>
public sealed class GetProvincesQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies that all supported filters are applied and the paged result is returned.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFiltersAreProvided_ShouldApplyFiltersAndReturnPagedResult()
    {
        // Arrange
        var countryId = _fixture.Create<Guid>();
        var regionId = _fixture.Create<Guid>();
        var createdAt = DateTime.UtcNow;
        var context = new GetProvincesQueryTestContext(useNpgsqlConnection: true);

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = Guid.NewGuid(),
                    ["Name"] = "Managua",
                    ["Code"] = "MN",
                    ["CountryId"] = countryId,
                    ["CountryName"] = "Nicaragua",
                    ["RegionId"] = regionId,
                    ["RegionName"] = "Pacific",
                    ["CreatedAt"] = createdAt
                }),
            FakeResultSet.FromScalar(21));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(
            new GetProvincesQuery(PageNumber: 0, PageSize: 100, Name: "  Mana  ", Code: " mn ", CountryId: countryId),
            CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Provinces retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(2);
        response.Data.PageSize.Should().Be(20);
        response.Data.TotalCount.Should().Be(21);
        response.Data.TotalPages.Should().Be(2);

        var item = response.Data.Items.Single();
        item.Name.Should().Be("Managua");
        item.Code.Should().Be("MN");
        item.CountryName.Should().Be("Nicaragua");
        item.RegionId.Should().Be(regionId);
        item.RegionName.Should().Be("Pacific");
        item.CreatedAt.Should().Be(createdAt);

        context.Connection.LastCommandText.Should().Contain("WHERE p.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("p.Name LIKE @Name");
        context.Connection.LastCommandText.Should().Contain("p.Code = @Code");
        context.Connection.LastCommandText.Should().Contain("p.CountryId = @CountryId");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");
        context.Connection.CapturedParameters["Name"].Should().Be("%Mana%");
        context.Connection.CapturedParameters["Code"].Should().Be("MN");
        context.Connection.CapturedParameters["CountryId"].Should().Be(countryId);
    }

    /// <summary>
    /// Verifies that empty results still return a valid paged response object.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoItemsMatch_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var context = new GetProvincesQueryTestContext(useNpgsqlConnection: false);
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "Name",
                "Code",
                "CountryId",
                "CountryName",
                "RegionId",
                "RegionName",
                "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetProvincesQuery(PageNumber: -1, PageSize: 0), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().BeEmpty();
        response.Data.PageNumber.Should().Be(2);
        response.Data.PageSize.Should().Be(5);
        response.Data.TotalCount.Should().Be(0);
        response.Data.TotalPages.Should().Be(0);
        context.Connection.LastCommandText.Should().Contain("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY");
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the query tests.
    /// </summary>
    private sealed class GetProvincesQueryTestContext
    {
        public GetProvincesQueryTestContext(bool useNpgsqlConnection)
        {
            Connection = useNpgsqlConnection ? new FakeNpgsqlDbConnection() : new FakeDbConnection();
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
            PaginationOptions = Options.Create(new PaginationSettings
            {
                DefaultPageNumber = 2,
                DefaultPageSize = 5,
                MaxPageSize = 20
            });
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; }
        public IOptions<PaginationSettings> PaginationOptions { get; }

        public GetProvincesQueryHandler CreateHandler()
        {
            return new GetProvincesQueryHandler(ConnectionFactoryMock.Object, PaginationOptions);
        }
    }
}
