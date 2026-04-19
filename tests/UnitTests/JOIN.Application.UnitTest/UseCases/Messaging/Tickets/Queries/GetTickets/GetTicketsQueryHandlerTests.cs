using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.Tickets.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.GetTickets;

/// <summary>
/// Contains the unit tests for the tenant-scoped ticket listing query.
/// These tests focus on filtering branches, pagination sanitization,
/// and the multi-tenant boundary enforced by the handler.
/// </summary>
public sealed class GetTicketsQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the current tenant identifier is missing.
    /// This protects the handler from performing reads without a valid tenant context.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = new GetTicketsQueryHandlerTestContext(Guid.Empty, useNpgsqlConnection: false);
        var query = new GetTicketsQuery();
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Verifies that all critical filters are translated into SQL and parameters.
    /// This test covers search text, status, project, visibility, and date range branches,
    /// while also validating tenant restriction and pagination sanitization.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFiltersAreProvided_ShouldApplyTenantFiltersAndReturnPagedTickets()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var projectId = _fixture.Create<Guid>();
        var fromDate = DateTime.UtcNow.Date.AddDays(-10);
        var toDate = DateTime.UtcNow.Date;

        var context = new GetTicketsQueryHandlerTestContext(companyId, useNpgsqlConnection: true);
        context.Connection.SetResults(
            CreateTicketItemsResultSet(companyId),
            FakeResultSet.FromScalar(21));

        var query = new GetTicketsQuery(
            PageNumber: 0,
            PageSize: 100,
            Search: "  printer  ",
            TicketStatusId: statusId,
            ProjectId: projectId,
            IsVisibleToExternals: true,
            FromDate: fromDate,
            ToDate: toDate);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Tickets retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(2);
        response.Data.PageSize.Should().Be(20);
        response.Data.TotalCount.Should().Be(21);
        response.Data.TotalPages.Should().Be(2);

        var item = response.Data.Items.Single();
        item.CompanyId.Should().Be(companyId);
        item.CompanyName.Should().Be("JOIN");
        item.Code.Should().Be("TICK-202604-0001");

        context.Connection.LastCommandText.Should().Contain("WHERE t.CompanyId = @TenantId AND t.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("t.Code LIKE @Search OR t.Name LIKE @Search OR t.Description LIKE @Search");
        context.Connection.LastCommandText.Should().Contain("t.TicketStatusId = @TicketStatusId");
        context.Connection.LastCommandText.Should().Contain("t.ProjectId = @ProjectId");
        context.Connection.LastCommandText.Should().Contain("t.IsVisibleToExternals = @IsVisibleToExternals");
        context.Connection.LastCommandText.Should().Contain("t.Created >= @FromDate");
        context.Connection.LastCommandText.Should().Contain("t.Created <= @ToDate");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");

        context.Connection.CapturedParameters["TenantId"].Should().Be(companyId);
        context.Connection.CapturedParameters["Search"].Should().Be("%printer%");
        context.Connection.CapturedParameters["TicketStatusId"].Should().Be(statusId);
        context.Connection.CapturedParameters["ProjectId"].Should().Be(projectId);
        context.Connection.CapturedParameters["IsVisibleToExternals"].Should().Be(true);
        context.Connection.CapturedParameters["FromDate"].Should().Be(fromDate);
        context.Connection.CapturedParameters["ToDate"].Should().Be(toDate);
        context.Connection.CapturedParameters["Offset"].Should().Be(20);
        context.Connection.CapturedParameters["PageSize"].Should().Be(20);
    }

    /// <summary>
    /// Verifies that an empty result set is returned successfully.
    /// This covers the branch where the handler must build a valid empty paged payload.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoTicketsMatch_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new GetTicketsQueryHandlerTestContext(companyId, useNpgsqlConnection: false);
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "CompanyId",
                "CompanyName",
                "Code",
                "Name",
                "TicketStatusId",
                "TicketStatusName",
                "TicketComplexityId",
                "TicketComplexityName",
                "CustomerId",
                "CustomerName",
                "AssignedToUserId",
                "AssignedToUserName",
                "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var query = new GetTicketsQuery(PageNumber: 1, PageSize: 5, Search: "no-match");
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().BeEmpty();
        response.Data.TotalCount.Should().Be(0);
        response.Data.TotalPages.Should().Be(0);
        context.Connection.LastCommandText.Should().Contain("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY");
    }

    /// <summary>
    /// Creates a fake result set containing a single ticket list item row.
    /// </summary>
    private static FakeResultSet CreateTicketItemsResultSet(Guid companyId)
    {
        return FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["CompanyId"] = companyId,
                ["CompanyName"] = "JOIN",
                ["Code"] = "TICK-202604-0001",
                ["Name"] = "Printer issue",
                ["TicketStatusId"] = Guid.NewGuid(),
                ["TicketStatusName"] = "Open",
                ["TicketComplexityId"] = Guid.NewGuid(),
                ["TicketComplexityName"] = "High",
                ["CustomerId"] = Guid.NewGuid(),
                ["CustomerName"] = "Contoso",
                ["AssignedToUserId"] = Guid.NewGuid(),
                ["AssignedToUserName"] = "Ana Torres",
                ["CreatedAt"] = DateTime.UtcNow
            });
    }

    /// <summary>
    /// Holds the reusable mocks and settings needed by the query handler tests.
    /// </summary>
    private sealed class GetTicketsQueryHandlerTestContext
    {
        public GetTicketsQueryHandlerTestContext(Guid companyId, bool useNpgsqlConnection)
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

        public GetTicketsQueryHandler CreateHandler()
        {
            return new GetTicketsQueryHandler(
                ConnectionFactoryMock.Object,
                CurrentUserServiceMock.Object,
                PaginationOptions);
        }
    }
}
