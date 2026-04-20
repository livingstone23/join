using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Common.StreetTypes.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.StreetTypes.Queries.GetStreetTypesPaged;

/// <summary>
/// Contains the unit tests for the street types paged query handler.
/// These tests verify filter application on both Name and Abbreviation, dialect-specific pagination,
/// empty results, and page sanitization using the shared Dapper fake infrastructure.
/// </summary>
public sealed class GetStreetTypesPagedQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies that the search filter is applied on both Name and Abbreviation columns
    /// and that the Npgsql LIMIT/OFFSET syntax is used.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSearchFilterProvidedWithNpgsql_ShouldApplyNameAndAbbreviationFilterAndReturnPagedStreetTypes()
    {
        // Arrange
        var context = new GetStreetTypesPagedQueryTestContext(useNpgsqlConnection: true);

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = Guid.NewGuid(),
                    ["Name"] = "Avenue",
                    ["Abbreviation"] = "Ave",
                    ["IsActive"] = true
                }),
            FakeResultSet.FromScalar(5));

        var query = new GetStreetTypesPagedQuery(
            PageNumber: 1,
            PageSize: 5,
            SearchTerm: "  Ave  ");

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Street types retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(1);
        response.Data.PageSize.Should().Be(5);
        response.Data.TotalCount.Should().Be(5);
        response.Data.TotalPages.Should().Be(1);

        context.Connection.LastCommandText.Should().Contain("WHERE st.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("st.Name LIKE @SearchTerm");
        context.Connection.LastCommandText.Should().Contain("st.Abbreviation LIKE @SearchTerm");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");

        context.Connection.CapturedParameters["SearchTerm"].Should().Be("%Ave%");
        context.Connection.CapturedParameters["Offset"].Should().Be(0);
        context.Connection.CapturedParameters["PageSize"].Should().Be(5);
    }

    /// <summary>
    /// Verifies that empty results are handled correctly with SQL Server OFFSET/FETCH syntax.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoStreetTypesMatchWithSqlServer_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var context = new GetStreetTypesPagedQueryTestContext(useNpgsqlConnection: false);

        context.Connection.SetResults(
            FakeResultSet.Empty("Id", "Name", "Abbreviation", "IsActive"),
            FakeResultSet.FromScalar(0));

        var query = new GetStreetTypesPagedQuery(
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
        var context = new GetStreetTypesPagedQueryTestContext(useNpgsqlConnection: false);

        context.Connection.SetResults(
            FakeResultSet.Empty("Id", "Name", "Abbreviation", "IsActive"),
            FakeResultSet.FromScalar(0));

        var query = new GetStreetTypesPagedQuery(
            PageNumber: -5,
            PageSize: 9999);

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
    /// Holds the reusable mocks and fake connection used by the street types paged query tests.
    /// </summary>
    private sealed class GetStreetTypesPagedQueryTestContext
    {
        public GetStreetTypesPagedQueryTestContext(bool useNpgsqlConnection)
        {
            Connection = useNpgsqlConnection ? new FakeNpgsqlDbConnection() : new FakeDbConnection();
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; }

        public GetStreetTypesPagedQueryHandler CreateHandler()
        {
            return new GetStreetTypesPagedQueryHandler(ConnectionFactoryMock.Object);
        }
    }
}
