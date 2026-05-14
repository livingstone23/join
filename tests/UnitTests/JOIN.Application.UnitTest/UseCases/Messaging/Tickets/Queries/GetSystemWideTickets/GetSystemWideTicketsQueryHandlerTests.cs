using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.Tickets.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.GetSystemWideTickets;

/// <summary>
/// Contains the unit tests for the system-wide ticket listing query.
/// The suite verifies that filters are applied correctly and that no tenant restriction is enforced.
/// </summary>
public sealed class GetSystemWideTicketsQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the happy path for a system-wide listing request.
    /// This test ensures the query ignores tenant scoping and applies cross-company filters correctly.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFiltersAreProvided_ShouldReturnSystemWideTicketsWithoutTenantRestriction()
    {
        // Arrange
        var statusId = _fixture.Create<Guid>();
        var projectId = _fixture.Create<Guid>();
        var fromDate = DateTime.UtcNow.Date.AddDays(-30);
        var toDate = DateTime.UtcNow.Date;

        var context = new GetSystemWideTicketsQueryHandlerTestContext();
        context.Connection.SetResults(
            CreateSystemWideItemsResultSet(),
            FakeResultSet.FromScalar(3));

        var query = new GetSystemWideTicketsQuery(
            PageNumber: 1,
            PageSize: 10,
            Search: "  urgent  ",
            TicketStatusId: statusId,
            ProjectId: projectId,
            IsVisibleToExternals: false,
            FromDate: fromDate,
            ToDate: toDate,
            CompanyName: "  JOIN  ");

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("System-wide tickets retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.TotalCount.Should().Be(3);
        response.Data.TotalPages.Should().Be(1);

        var item = response.Data.Items.Single();
        item.CompanyName.Should().Be("JOIN Corp");
        item.Name.Should().Be("Urgent production issue");

        context.Connection.LastCommandText.Should().Contain("WHERE 1 = 1");
        context.Connection.LastCommandText.Should().Contain("co.Name LIKE @CompanyName");
        context.Connection.LastCommandText.Should().Contain("t.Code LIKE @Search OR t.Name LIKE @Search OR t.Description LIKE @Search");
        context.Connection.LastCommandText.Should().Contain("t.TicketStatusId = @TicketStatusId");
        context.Connection.LastCommandText.Should().Contain("t.ProjectId = @ProjectId");
        context.Connection.LastCommandText.Should().Contain("t.Created >= @FromDate");
        context.Connection.LastCommandText.Should().Contain("t.Created <= @ToDate");
        context.Connection.LastCommandText.Should().Contain("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY");
        context.Connection.LastCommandText.Should().NotContain("t.CompanyId = @TenantId");
        context.Connection.LastCommandText.Should().NotContain("@TenantId");

        context.Connection.CapturedParameters.ContainsKey("TenantId").Should().BeFalse();
        context.Connection.CapturedParameters["Search"].Should().Be("%urgent%");
        context.Connection.CapturedParameters["CompanyName"].Should().Be("%JOIN%");
        context.Connection.CapturedParameters["TicketStatusId"].Should().Be(statusId);
        context.Connection.CapturedParameters["ProjectId"].Should().Be(projectId);
    }

    /// <summary>
    /// Verifies that the handler returns a valid empty response when no records match the filters.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoTicketsMatch_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var context = new GetSystemWideTicketsQueryHandlerTestContext();
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
                "PersonId",
                "PersonName",
                "AssignedToUserId",
                "AssignedToUserName",
                "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var query = new GetSystemWideTicketsQuery(PageNumber: 0, PageSize: -1, CompanyName: "unknown");
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
    }

    /// <summary>
    /// Creates a fake result set containing one system-wide ticket row.
    /// </summary>
    private static FakeResultSet CreateSystemWideItemsResultSet()
    {
        return FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["CompanyId"] = Guid.NewGuid(),
                ["CompanyName"] = "JOIN Corp",
                ["Code"] = "TICK-202604-0042",
                ["Name"] = "Urgent production issue",
                ["TicketStatusId"] = Guid.NewGuid(),
                ["TicketStatusName"] = "Escalated",
                ["TicketComplexityId"] = Guid.NewGuid(),
                ["TicketComplexityName"] = "Critical",
                ["PersonId"] = Guid.NewGuid(),
                ["PersonName"] = "Fabrikam",
                ["AssignedToUserId"] = Guid.NewGuid(),
                ["AssignedToUserName"] = "Luis Gomez",
                ["CreatedAt"] = DateTime.UtcNow
            });
    }

    /// <summary>
    /// Holds the reusable connection factory and pagination settings for the system-wide query tests.
    /// </summary>
    private sealed class GetSystemWideTicketsQueryHandlerTestContext
    {
        public GetSystemWideTicketsQueryHandlerTestContext()
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
            PaginationOptions = Options.Create(new PaginationSettings
            {
                DefaultPageNumber = 1,
                DefaultPageSize = 10,
                MaxPageSize = 50
            });
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; } = new FakeDbConnection();
        public IOptions<PaginationSettings> PaginationOptions { get; }

        public GetSystemWideTicketsQueryHandler CreateHandler()
        {
            return new GetSystemWideTicketsQueryHandler(
                ConnectionFactoryMock.Object,
                PaginationOptions);
        }
    }
}
