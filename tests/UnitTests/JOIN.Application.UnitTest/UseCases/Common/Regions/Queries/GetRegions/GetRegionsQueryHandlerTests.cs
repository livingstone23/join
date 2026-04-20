using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Common.Regions.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Regions.Queries.GetRegions;

/// <summary>
/// Contains the unit tests for the region listing query.
/// These tests verify filtering, pagination sanitization, and empty-result behavior.
/// </summary>
public sealed class GetRegionsQueryHandlerTests
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
        var createdAt = DateTime.UtcNow;
        var context = new GetRegionsQueryTestContext(useNpgsqlConnection: true);

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = Guid.NewGuid(),
                    ["Name"] = "North",
                    ["Code"] = "CA",
                    ["CountryId"] = countryId,
                    ["CountryName"] = "Canada",
                    ["CreatedAt"] = createdAt
                }),
            FakeResultSet.FromScalar(21));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(
            new GetRegionsQuery(PageNumber: 0, PageSize: 100, Name: "  North  ", Code: " ca ", CountryId: countryId),
            CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Regions retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(2);
        response.Data.PageSize.Should().Be(20);
        response.Data.TotalCount.Should().Be(21);
        response.Data.TotalPages.Should().Be(2);

        var item = response.Data.Items.Single();
        item.Name.Should().Be("North");
        item.Code.Should().Be("CA");
        item.CountryId.Should().Be(countryId);
        item.CountryName.Should().Be("Canada");
        item.CreatedAt.Should().Be(createdAt);

        context.Connection.LastCommandText.Should().Contain("WHERE r.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("r.Name LIKE @Name");
        context.Connection.LastCommandText.Should().Contain("r.Code = @Code");
        context.Connection.LastCommandText.Should().Contain("r.CountryId = @CountryId");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");
        context.Connection.CapturedParameters["Name"].Should().Be("%North%");
        context.Connection.CapturedParameters["Code"].Should().Be("CA");
        context.Connection.CapturedParameters["CountryId"].Should().Be(countryId);
        context.Connection.CapturedParameters["Offset"].Should().Be(20);
    }

    /// <summary>
    /// Verifies that empty results still return a valid paged response object.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoItemsMatch_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var context = new GetRegionsQueryTestContext(useNpgsqlConnection: false);
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "Name",
                "Code",
                "CountryId",
                "CountryName",
                "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetRegionsQuery(PageNumber: -1, PageSize: 0), CancellationToken.None);

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
    private sealed class GetRegionsQueryTestContext
    {
        public GetRegionsQueryTestContext(bool useNpgsqlConnection)
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

        public GetRegionsQueryHandler CreateHandler()
        {
            return new GetRegionsQueryHandler(ConnectionFactoryMock.Object, PaginationOptions);
        }
    }
}
