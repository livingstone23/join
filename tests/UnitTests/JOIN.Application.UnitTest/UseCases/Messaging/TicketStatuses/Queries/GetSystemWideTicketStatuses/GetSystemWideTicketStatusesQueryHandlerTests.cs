using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketStatuses.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketStatuses.Queries.GetSystemWideTicketStatuses;

/// <summary>
/// Contains the unit tests for the system-wide ticket status query handler.
/// These tests validate global access without tenant restriction, filter generation,
/// pagination sanitization, and empty result handling using the fake Dapper infrastructure.
/// </summary>
public sealed class GetSystemWideTicketStatusesQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies that all supported filters are translated into SQL and parameters,
    /// while the query remains system-wide without a tenant restriction.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFiltersAreProvided_ShouldApplyFiltersWithoutTenantRestriction()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var createdAt = DateTime.UtcNow;
        var context = new GetSystemWideTicketStatusesQueryTestContext(useNpgsqlConnection: true);

        context.Connection.SetResults(
            CreateItemsResultSet(companyId, createdAt),
            FakeResultSet.FromScalar(21));

        var query = new GetSystemWideTicketStatusesQuery(
            PageNumber: 0,
            PageSize: 100,
            Name: "  Open  ",
            IsActive: true,
            IsInitial: false,
            IsPaused: false,
            IsFinal: true,
            CompanyName: "  JOIN  ",
            Code: 10);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("System-wide ticket statuses retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(2);
        response.Data.PageSize.Should().Be(20);
        response.Data.TotalCount.Should().Be(21);
        response.Data.TotalPages.Should().Be(2);

        var item = response.Data.Items.Single();
        item.CompanyId.Should().Be(companyId);
        item.CompanyName.Should().Be("JOIN CRM");
        item.Name.Should().Be("Open");
        item.Code.Should().Be(10);
        item.IsActive.Should().BeTrue();
        item.IsFinal.Should().BeTrue();

        context.Connection.LastCommandText.Should().Contain("WHERE ts.GcRecord = 0 AND c.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("ts.Name LIKE @Name");
        context.Connection.LastCommandText.Should().Contain("ts.IsActive = @IsActive");
        context.Connection.LastCommandText.Should().Contain("ts.IsInitial = @IsInitial");
        context.Connection.LastCommandText.Should().Contain("ts.IsPaused = @IsPaused");
        context.Connection.LastCommandText.Should().Contain("ts.IsFinal = @IsFinal");
        context.Connection.LastCommandText.Should().Contain("c.Name LIKE @CompanyName");
        context.Connection.LastCommandText.Should().Contain("ts.Code = @Code");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");
        context.Connection.LastCommandText.Should().NotContain("TenantId");

        context.Connection.CapturedParameters["Name"].Should().Be("%Open%");
        context.Connection.CapturedParameters["IsActive"].Should().Be(true);
        context.Connection.CapturedParameters["IsInitial"].Should().Be(false);
        context.Connection.CapturedParameters["IsPaused"].Should().Be(false);
        context.Connection.CapturedParameters["IsFinal"].Should().Be(true);
        context.Connection.CapturedParameters["CompanyName"].Should().Be("%JOIN%");
        context.Connection.CapturedParameters["Code"].Should().Be(10);
        context.Connection.CapturedParameters["Offset"].Should().Be(20);
        context.Connection.CapturedParameters["PageSize"].Should().Be(20);
    }

    /// <summary>
    /// Verifies that an empty system-wide query still returns a valid paged payload.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoStatusesMatch_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var context = new GetSystemWideTicketStatusesQueryTestContext(useNpgsqlConnection: false);
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "CompanyId",
                "CompanyName",
                "Name",
                "Description",
                "Code",
                "IsActive",
                "IsInitial",
                "IsPaused",
                "IsFinal",
                "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var query = new GetSystemWideTicketStatusesQuery(PageNumber: -5, PageSize: 0, Name: "missing");
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

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
    /// Creates a fake result set containing one ticket status row.
    /// </summary>
    private static FakeResultSet CreateItemsResultSet(Guid companyId, DateTime createdAt)
    {
        return FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["CompanyId"] = companyId,
                ["CompanyName"] = "JOIN CRM",
                ["Name"] = "Open",
                ["Description"] = "Initial active status",
                ["Code"] = 10,
                ["IsActive"] = true,
                ["IsInitial"] = false,
                ["IsPaused"] = false,
                ["IsFinal"] = true,
                ["CreatedAt"] = createdAt
            });
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the query tests.
    /// </summary>
    private sealed class GetSystemWideTicketStatusesQueryTestContext
    {
        public GetSystemWideTicketStatusesQueryTestContext(bool useNpgsqlConnection)
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

        public GetSystemWideTicketStatusesQueryHandler CreateHandler()
        {
            return new GetSystemWideTicketStatusesQueryHandler(
                ConnectionFactoryMock.Object,
                PaginationOptions);
        }
    }
}
