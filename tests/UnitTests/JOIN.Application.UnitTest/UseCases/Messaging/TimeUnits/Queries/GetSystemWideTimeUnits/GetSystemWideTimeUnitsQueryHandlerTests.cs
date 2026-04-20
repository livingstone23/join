using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.TimeUnits.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TimeUnits.Queries.GetSystemWideTimeUnits;

/// <summary>
/// Contains the unit tests for the system-wide time unit listing query.
/// These tests verify global filtering, pagination sanitization, and provider-specific SQL generation.
/// </summary>
public sealed class GetSystemWideTimeUnitsQueryHandlerTests
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
        var context = new GetSystemWideTimeUnitsQueryTestContext(useNpgsqlConnection: true);

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = Guid.NewGuid(),
                    ["CompanyId"] = companyId,
                    ["CompanyName"] = "JOIN CRM",
                    ["Name"] = "Hours",
                    ["Code"] = 1,
                    ["IsActive"] = true,
                    ["CreatedAt"] = createdAt
                }),
            FakeResultSet.FromScalar(21));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(
            new GetSystemWideTimeUnitsQuery(PageNumber: 0, PageSize: 100, Name: "  Hour  ", IsActive: true, CompanyName: "  JOIN  "),
            CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("System-wide time units retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(2);
        response.Data.PageSize.Should().Be(20);
        response.Data.TotalCount.Should().Be(21);
        response.Data.TotalPages.Should().Be(2);

        context.Connection.LastCommandText.Should().Contain("WHERE 1 = 1");
        context.Connection.LastCommandText.Should().Contain("tu.Name LIKE @Name");
        context.Connection.LastCommandText.Should().Contain("tu.IsActive = @IsActive");
        context.Connection.LastCommandText.Should().Contain("c.Name LIKE @CompanyName");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");
        context.Connection.LastCommandText.Should().NotContain("tu.CompanyId = @TenantId");

        context.Connection.CapturedParameters["Name"].Should().Be("%Hour%");
        context.Connection.CapturedParameters["IsActive"].Should().Be(true);
        context.Connection.CapturedParameters["CompanyName"].Should().Be("%JOIN%");
    }

    /// <summary>
    /// Verifies that an empty result still returns a valid paged payload.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoItemsMatch_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var context = new GetSystemWideTimeUnitsQueryTestContext(useNpgsqlConnection: false);
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "CompanyId",
                "CompanyName",
                "Name",
                "Code",
                "IsActive",
                "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetSystemWideTimeUnitsQuery(PageNumber: -1, PageSize: 0), CancellationToken.None);

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
    private sealed class GetSystemWideTimeUnitsQueryTestContext
    {
        public GetSystemWideTimeUnitsQueryTestContext(bool useNpgsqlConnection)
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

        public GetSystemWideTimeUnitsQueryHandler CreateHandler()
        {
            return new GetSystemWideTimeUnitsQueryHandler(
                ConnectionFactoryMock.Object,
                PaginationOptions);
        }
    }
}
