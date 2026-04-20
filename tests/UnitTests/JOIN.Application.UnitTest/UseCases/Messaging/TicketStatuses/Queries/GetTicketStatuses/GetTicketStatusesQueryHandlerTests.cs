using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketStatuses.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketStatuses.Queries.GetTicketStatuses;

/// <summary>
/// Contains the unit tests for the tenant-scoped ticket status query.
/// These tests verify CompanyId protection, tenant restriction, pagination behavior,
/// and empty result handling with the fake Dapper infrastructure.
/// </summary>
public sealed class GetTicketStatusesQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the CompanyId claim is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = new GetTicketStatusesQueryTestContext(Guid.Empty, useNpgsqlConnection: false);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetTicketStatusesQuery(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        response.Errors.Should().Contain("The authenticated token must contain a valid CompanyId claim.");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Verifies that the query applies tenant restriction, supported filters,
    /// and sanitized pagination settings for the PostgreSQL branch.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFiltersAreProvided_ShouldApplyTenantFiltersAndReturnPagedStatuses()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var createdAt = DateTime.UtcNow;
        var context = new GetTicketStatusesQueryTestContext(companyId, useNpgsqlConnection: true);

        context.Connection.SetResults(
            CreateItemsResultSet(companyId, createdAt),
            FakeResultSet.FromScalar(21));

        var query = new GetTicketStatusesQuery(
            PageNumber: 0,
            PageSize: 100,
            Name: "  Open  ",
            IsActive: true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket statuses retrieved successfully.");
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
        item.IsActive.Should().BeTrue();

        context.Connection.LastCommandText.Should().Contain("WHERE ts.CompanyId = @TenantId AND ts.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("ts.Name LIKE @Name");
        context.Connection.LastCommandText.Should().Contain("ts.IsActive = @IsActive");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");

        context.Connection.CapturedParameters["TenantId"].Should().Be(companyId);
        context.Connection.CapturedParameters["Name"].Should().Be("%Open%");
        context.Connection.CapturedParameters["IsActive"].Should().Be(true);
        context.Connection.CapturedParameters["Offset"].Should().Be(20);
        context.Connection.CapturedParameters["PageSize"].Should().Be(20);
    }

    /// <summary>
    /// Verifies that an empty tenant-scoped query still returns a valid paged result.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoStatusesMatch_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new GetTicketStatusesQueryTestContext(companyId, useNpgsqlConnection: false);
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

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetTicketStatusesQuery(PageNumber: -1, PageSize: 0, Name: "missing"), CancellationToken.None);

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
    /// Creates a fake result set containing one tenant-scoped status row.
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
                ["Description"] = "Tenant status",
                ["Code"] = 10,
                ["IsActive"] = true,
                ["IsInitial"] = true,
                ["IsPaused"] = false,
                ["IsFinal"] = false,
                ["CreatedAt"] = createdAt
            });
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the query tests.
    /// </summary>
    private sealed class GetTicketStatusesQueryTestContext
    {
        public GetTicketStatusesQueryTestContext(Guid companyId, bool useNpgsqlConnection)
        {
            Connection = useNpgsqlConnection ? new FakeNpgsqlDbConnection() : new FakeDbConnection();
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
            PaginationOptions = Options.Create(new PaginationSettings
            {
                DefaultPageNumber = 2,
                DefaultPageSize = 5,
                MaxPageSize = 20
            });
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; }
        public IOptions<PaginationSettings> PaginationOptions { get; }

        public GetTicketStatusesQueryHandler CreateHandler()
        {
            return new GetTicketStatusesQueryHandler(
                ConnectionFactoryMock.Object,
                PaginationOptions,
                CurrentUserServiceMock.Object);
        }
    }
}
