using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketComplexities.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketComplexities.Queries.GetSystemWideTicketComplexities;

/// <summary>
/// Contains the unit tests for the system-wide ticket complexity listing query.
/// These tests verify global filtering, pagination sanitization, and provider-specific SQL generation.
/// </summary>
public sealed class GetSystemWideTicketComplexitiesQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies that the handler applies all supported filters without tenant restriction.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFiltersAreProvided_ShouldApplyFiltersWithoutTenantRestriction()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var createdAt = DateTime.UtcNow;
        var context = new GetSystemWideTicketComplexitiesQueryTestContext(useNpgsqlConnection: true);

        context.Connection.SetResults(
            CreateItemsResultSet(companyId, createdAt),
            FakeResultSet.FromScalar(21));

        var query = new GetSystemWideTicketComplexitiesQuery(
            PageNumber: 0,
            PageSize: 100,
            Name: "  Critical  ",
            IsActive: true,
            CompanyName: "  JOIN  ");

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("System-wide ticket complexities retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(2);
        response.Data.PageSize.Should().Be(20);
        response.Data.TotalCount.Should().Be(21);
        response.Data.TotalPages.Should().Be(2);

        var item = response.Data.Items.Single();
        item.CompanyId.Should().Be(companyId);
        item.CompanyName.Should().Be("JOIN CRM");
        item.Name.Should().Be("Critical");
        item.Code.Should().Be(90);

        context.Connection.LastCommandText.Should().Contain("WHERE 1 = 1");
        context.Connection.LastCommandText.Should().Contain("tc.Name LIKE @Name");
        context.Connection.LastCommandText.Should().Contain("tc.IsActive = @IsActive");
        context.Connection.LastCommandText.Should().Contain("c.Name LIKE @CompanyName");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");
        context.Connection.LastCommandText.Should().NotContain("tc.CompanyId = @TenantId");

        context.Connection.CapturedParameters["Name"].Should().Be("%Critical%");
        context.Connection.CapturedParameters["IsActive"].Should().Be(true);
        context.Connection.CapturedParameters["CompanyName"].Should().Be("%JOIN%");
        context.Connection.CapturedParameters["Offset"].Should().Be(20);
        context.Connection.CapturedParameters["PageSize"].Should().Be(20);
    }

    /// <summary>
    /// Verifies that an empty result still returns a valid paged payload.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoItemsMatch_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var context = new GetSystemWideTicketComplexitiesQueryTestContext(useNpgsqlConnection: false);
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "CompanyId",
                "CompanyName",
                "Name",
                "Description",
                "Code",
                "ResolutionTimeUnits",
                "TimeUnitId",
                "IsActive",
                "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetSystemWideTicketComplexitiesQuery(PageNumber: -1, PageSize: 0), CancellationToken.None);

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
    /// Creates a one-row fake result set for the listing query.
    /// </summary>
    private static FakeResultSet CreateItemsResultSet(Guid companyId, DateTime createdAt)
    {
        return FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["CompanyId"] = companyId,
                ["CompanyName"] = "JOIN CRM",
                ["Name"] = "Critical",
                ["Description"] = "High urgency complexity",
                ["Code"] = 90,
                ["ResolutionTimeUnits"] = 4,
                ["TimeUnitId"] = Guid.NewGuid(),
                ["IsActive"] = true,
                ["CreatedAt"] = createdAt
            });
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the query tests.
    /// </summary>
    private sealed class GetSystemWideTicketComplexitiesQueryTestContext
    {
        public GetSystemWideTicketComplexitiesQueryTestContext(bool useNpgsqlConnection)
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

        public GetSystemWideTicketComplexitiesQueryHandler CreateHandler()
        {
            return new GetSystemWideTicketComplexitiesQueryHandler(
                ConnectionFactoryMock.Object,
                PaginationOptions);
        }
    }
}
