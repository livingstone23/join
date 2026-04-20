using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Common.Countries.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Countries.Queries.GetCountriesPaged;

/// <summary>
/// Contains the unit tests for the countries paged query handler.
/// These tests verify filter application, dialect-specific pagination, empty results,
/// and page sanitization using the shared Dapper fake infrastructure.
/// </summary>
public sealed class GetCountriesPagedQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies that the name search filter is applied and the Npgsql LIMIT/OFFSET syntax is used.
    /// Countries only filter on Name (not IsoCode).
    /// </summary>
    [Fact]
    public async Task Handle_WhenNameFilterProvidedWithNpgsql_ShouldApplyNameFilterAndReturnPagedCountries()
    {
        // Arrange
        var context = new GetCountriesPagedQueryTestContext(useNpgsqlConnection: true);

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = Guid.NewGuid(),
                    ["Name"] = "Panama",
                    ["IsoCode"] = "PA"
                }),
            FakeResultSet.FromScalar(3));

        var query = new GetCountriesPagedQuery(
            PageNumber: 1,
            PageSize: 5,
            SearchTerm: "  pan  ");

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Countries retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(1);
        response.Data.PageSize.Should().Be(5);
        response.Data.TotalCount.Should().Be(3);
        response.Data.TotalPages.Should().Be(1);

        context.Connection.LastCommandText.Should().Contain("WHERE c.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("c.Name LIKE @SearchTerm");
        context.Connection.LastCommandText.Should().NotContain("c.IsoCode LIKE @SearchTerm");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");

        context.Connection.CapturedParameters["SearchTerm"].Should().Be("%pan%");
        context.Connection.CapturedParameters["Offset"].Should().Be(0);
        context.Connection.CapturedParameters["PageSize"].Should().Be(5);
    }

    /// <summary>
    /// Verifies that empty results are handled correctly with SQL Server OFFSET/FETCH syntax.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoCountriesMatchWithSqlServer_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var context = new GetCountriesPagedQueryTestContext(useNpgsqlConnection: false);

        context.Connection.SetResults(
            FakeResultSet.Empty("Id", "Name", "IsoCode"),
            FakeResultSet.FromScalar(0));

        var query = new GetCountriesPagedQuery(
            PageNumber: 1,
            PageSize: 10,
            SearchTerm: "xyz");

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().BeEmpty();
        response.Data.PageNumber.Should().Be(1);
        response.Data.PageSize.Should().Be(10);
        response.Data.TotalCount.Should().Be(0);
        response.Data.TotalPages.Should().Be(0);

        context.Connection.LastCommandText.Should().Contain("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY");
    }

    /// <summary>
    /// Verifies that out-of-range pagination values are sanitized to safe defaults.
    /// PageNumber less than 1 resets to 1; PageSize less than 1 resets to 10; PageSize greater than 50 caps to 50.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPaginationValuesAreOutOfRange_ShouldSanitizeToSafeDefaults()
    {
        // Arrange
        var context = new GetCountriesPagedQueryTestContext(useNpgsqlConnection: false);

        context.Connection.SetResults(
            FakeResultSet.Empty("Id", "Name", "IsoCode"),
            FakeResultSet.FromScalar(0));

        var query = new GetCountriesPagedQuery(
            PageNumber: 0,
            PageSize: 999);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.Data.Should().NotBeNull();
        response.Data!.PageNumber.Should().Be(1);
        response.Data.PageSize.Should().Be(50);

        context.Connection.CapturedParameters["Offset"].Should().Be(0);
        context.Connection.CapturedParameters["PageSize"].Should().Be(50);
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the countries paged query tests.
    /// </summary>
    private sealed class GetCountriesPagedQueryTestContext
    {
        public GetCountriesPagedQueryTestContext(bool useNpgsqlConnection)
        {
            Connection = useNpgsqlConnection ? new FakeNpgsqlDbConnection() : new FakeDbConnection();
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; }

        public GetCountriesPagedQueryHandler CreateHandler()
        {
            return new GetCountriesPagedQueryHandler(ConnectionFactoryMock.Object);
        }
    }
}
