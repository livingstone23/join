using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.SystemModules.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.SystemModules.Queries.GetSystemModules;

/// <summary>
/// Contains the unit tests for the system module listing query.
/// These tests verify filter application, pagination sanitization,
/// and empty result handling with the shared fake Dapper infrastructure.
/// </summary>
public sealed class GetSystemModulesQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies that the handler applies all supported filters and returns a paged result.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFiltersAreProvided_ShouldApplyFiltersAndReturnPagedModules()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;
        var context = new GetSystemModulesQueryTestContext(useNpgsqlConnection: true);

        context.Connection.SetResults(
            CreateItemsResultSet(createdAt),
            FakeResultSet.FromScalar(21));

        var query = new GetSystemModulesQuery(
            PageNumber: 0,
            PageSize: 100,
            Name: "  CRM  ",
            IsActive: true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("System modules retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(2);
        response.Data.PageSize.Should().Be(20);
        response.Data.TotalCount.Should().Be(21);
        response.Data.TotalPages.Should().Be(2);

        var item = response.Data.Items.Single();
        item.Name.Should().Be("CRM");
        item.Description.Should().Be("Customer management");
        item.Icon.Should().Be("fa-users");
        item.IsActive.Should().BeTrue();
        item.CreatedAt.Should().Be(createdAt);

        context.Connection.LastCommandText.Should().Contain("WHERE sm.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("sm.Name LIKE @Name");
        context.Connection.LastCommandText.Should().Contain("sm.IsActive = @IsActive");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");

        context.Connection.CapturedParameters["Name"].Should().Be("%CRM%");
        context.Connection.CapturedParameters["IsActive"].Should().Be(true);
        context.Connection.CapturedParameters["Offset"].Should().Be(20);
        context.Connection.CapturedParameters["PageSize"].Should().Be(20);
    }

    /// <summary>
    /// Verifies that an empty result still returns a valid paged payload.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoModulesMatch_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var context = new GetSystemModulesQueryTestContext(useNpgsqlConnection: false);
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "Name",
                "Description",
                "Icon",
                "IsActive",
                "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(
            new GetSystemModulesQuery(PageNumber: -1, PageSize: 0, Name: "missing"),
            CancellationToken.None);

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
    /// Creates a fake one-row result set for the list query.
    /// </summary>
    private static FakeResultSet CreateItemsResultSet(DateTime createdAt)
    {
        return FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["Name"] = "CRM",
                ["Description"] = "Customer management",
                ["Icon"] = "fa-users",
                ["IsActive"] = true,
                ["CreatedAt"] = createdAt
            });
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the query tests.
    /// </summary>
    private sealed class GetSystemModulesQueryTestContext
    {
        public GetSystemModulesQueryTestContext(bool useNpgsqlConnection)
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

        public GetSystemModulesQueryHandler CreateHandler()
        {
            return new GetSystemModulesQueryHandler(
                ConnectionFactoryMock.Object,
                PaginationOptions);
        }
    }
}
