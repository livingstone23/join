using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Common.Municipalities.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Municipalities.Queries.GetMunicipalities;

/// <summary>
/// Contains the unit tests for the municipality listing query.
/// These tests verify filtering, pagination sanitization, joins, and empty results.
/// </summary>
public sealed class GetMunicipalitiesQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies that the handler applies all supported filters and returns a paged result.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFiltersAreProvided_ShouldApplyFiltersAndReturnPagedResult()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var createdAt = DateTime.UtcNow;
        var context = new GetMunicipalitiesQueryTestContext(useNpgsqlConnection: true);

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = Guid.NewGuid(),
                    ["Name"] = "Managua",
                    ["Code"] = "MN",
                    ["ProvinceId"] = provinceId,
                    ["ProvinceName"] = "Managua Province",
                    ["CreatedAt"] = createdAt
                }),
            FakeResultSet.FromScalar(21));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(
            new GetMunicipalitiesQuery(PageNumber: 0, PageSize: 100, Name: "  Mana  ", Code: " mn ", ProvinceId: provinceId),
            CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Municipalities retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(2);
        response.Data.PageSize.Should().Be(20);
        response.Data.TotalCount.Should().Be(21);
        response.Data.TotalPages.Should().Be(2);

        var item = response.Data.Items.Single();
        item.Name.Should().Be("Managua");
        item.Code.Should().Be("MN");
        item.ProvinceId.Should().Be(provinceId);
        item.ProvinceName.Should().Be("Managua Province");
        item.CreatedAt.Should().Be(createdAt);

        context.Connection.LastCommandText.Should().Contain("WHERE m.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("INNER JOIN Common.Provinces p");
        context.Connection.LastCommandText.Should().Contain("m.Name LIKE @Name");
        context.Connection.LastCommandText.Should().Contain("m.Code = @Code");
        context.Connection.LastCommandText.Should().Contain("m.ProvinceId = @ProvinceId");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");
        context.Connection.CapturedParameters["Name"].Should().Be("%Mana%");
        context.Connection.CapturedParameters["Code"].Should().Be("MN");
        context.Connection.CapturedParameters["ProvinceId"].Should().Be(provinceId);
        context.Connection.CapturedParameters["Offset"].Should().Be(20);
    }

    /// <summary>
    /// Verifies that empty results still return a valid paged response.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoItemsMatch_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var context = new GetMunicipalitiesQueryTestContext(useNpgsqlConnection: false);
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "Name",
                "Code",
                "ProvinceId",
                "ProvinceName",
                "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetMunicipalitiesQuery(PageNumber: -1, PageSize: 0), CancellationToken.None);

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
    private sealed class GetMunicipalitiesQueryTestContext
    {
        public GetMunicipalitiesQueryTestContext(bool useNpgsqlConnection)
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

        public GetMunicipalitiesQueryHandler CreateHandler()
        {
            return new GetMunicipalitiesQueryHandler(ConnectionFactoryMock.Object, PaginationOptions);
        }
    }
}
