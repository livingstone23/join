using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Common.CommunicationChannels.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.CommunicationChannels.Queries.GetCommunicationChannelsPaged;

/// <summary>
/// Contains the unit tests for the communication channels paged query handler.
/// These tests verify filter application, dialect-specific pagination, empty results,
/// and page sanitization using the shared Dapper fake infrastructure.
/// </summary>
public sealed class GetCommunicationChannelsPagedQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies that search filters are applied and the Npgsql LIMIT/OFFSET syntax is used.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFiltersAreProvidedWithNpgsql_ShouldApplyFiltersAndReturnPagedChannels()
    {
        // Arrange
        var context = new GetCommunicationChannelsPagedQueryTestContext(useNpgsqlConnection: true);

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = Guid.NewGuid(),
                    ["Name"] = "WhatsApp",
                    ["Provider"] = "Twilio",
                    ["Code"] = "WA-001",
                    ["IsActive"] = true
                }),
            FakeResultSet.FromScalar(7));

        var query = new GetCommunicationChannelsPagedQuery(
            PageNumber: 2,
            PageSize: 5,
            SearchTerm: "  What  ");

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Communication channels retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(2);
        response.Data.PageSize.Should().Be(5);
        response.Data.TotalCount.Should().Be(7);
        response.Data.TotalPages.Should().Be(2);

        var item = response.Data.Items.Single();
        item.Name.Should().Be("WhatsApp");
        item.IsActive.Should().BeTrue();

        context.Connection.LastCommandText.Should().Contain("WHERE cc.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("cc.Name LIKE @SearchTerm");
        context.Connection.LastCommandText.Should().Contain("cc.Provider LIKE @SearchTerm");
        context.Connection.LastCommandText.Should().Contain("cc.Code LIKE @SearchTerm");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");

        context.Connection.CapturedParameters["SearchTerm"].Should().Be("%What%");
        context.Connection.CapturedParameters["Offset"].Should().Be(5);
        context.Connection.CapturedParameters["PageSize"].Should().Be(5);
    }

    /// <summary>
    /// Verifies that empty results are handled correctly with SQL Server OFFSET/FETCH syntax.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoChannelsMatchWithSqlServer_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var context = new GetCommunicationChannelsPagedQueryTestContext(useNpgsqlConnection: false);

        context.Connection.SetResults(
            FakeResultSet.Empty("Id", "Name", "Provider", "Code", "IsActive"),
            FakeResultSet.FromScalar(0));

        var query = new GetCommunicationChannelsPagedQuery(
            PageNumber: 1,
            PageSize: 10,
            SearchTerm: "missing");

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
        var context = new GetCommunicationChannelsPagedQueryTestContext(useNpgsqlConnection: false);

        context.Connection.SetResults(
            FakeResultSet.Empty("Id", "Name", "Provider", "Code", "IsActive"),
            FakeResultSet.FromScalar(0));

        var query = new GetCommunicationChannelsPagedQuery(
            PageNumber: -1,
            PageSize: 200);

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
    /// Holds the reusable mocks and fake connection used by the communication channels paged query tests.
    /// </summary>
    private sealed class GetCommunicationChannelsPagedQueryTestContext
    {
        public GetCommunicationChannelsPagedQueryTestContext(bool useNpgsqlConnection)
        {
            Connection = useNpgsqlConnection ? new FakeNpgsqlDbConnection() : new FakeDbConnection();
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; }

        public GetCommunicationChannelsPagedQueryHandler CreateHandler()
        {
            return new GetCommunicationChannelsPagedQueryHandler(ConnectionFactoryMock.Object);
        }
    }
}
