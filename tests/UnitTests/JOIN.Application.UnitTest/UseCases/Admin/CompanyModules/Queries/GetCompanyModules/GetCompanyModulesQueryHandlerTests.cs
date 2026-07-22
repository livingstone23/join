using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.CompanyModules.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.CompanyModules.Queries.GetCompanyModules;

/// <summary>
/// Contains the unit tests for the tenant-scoped company module listing query.
/// These tests verify company validation, search filters, pagination boundaries,
/// and the empty result path using the shared fake Dapper infrastructure.
/// </summary>
public sealed class GetCompanyModulesQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies that an empty <see cref="Guid"/> is treated as a valid filter
    /// (the query accepts an optional CompanyId scope) and returns an empty paged
    /// result instead of an error.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmptyGuid_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var context = new GetCompanyModulesQueryTestContext(useNpgsqlConnection: false);
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "CompanyId",
                "CompanyName",
                "ModuleId",
                "ModuleName",
                "IsActive",
                "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var query = new GetCompanyModulesQuery(Guid.Empty);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().BeEmpty();
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler applies the tenant restriction, requested search filters,
    /// and sanitized pagination values while returning the matching company modules.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFiltersAreProvided_ShouldApplyFiltersAndReturnPagedModules()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var createdFrom = DateTime.UtcNow.Date.AddDays(-10);
        var createdTo = DateTime.UtcNow.Date;
        var createdAt = DateTime.UtcNow;

        var context = new GetCompanyModulesQueryTestContext(useNpgsqlConnection: true);
        context.Connection.SetResults(
            CreateCompanyModuleItemsResultSet(companyId, createdAt),
            FakeResultSet.FromScalar(21));

        var query = new GetCompanyModulesQuery(
            CompanyId: companyId,
            PageNumber: 0,
            PageSize: 100,
            CompanyName: "  JOIN  ",
            ModuleName: "  Support  ",
            CreatedFrom: createdFrom,
            CreatedTo: createdTo);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Company modules retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(2);
        response.Data.PageSize.Should().Be(20);
        response.Data.TotalCount.Should().Be(21);
        response.Data.TotalPages.Should().Be(2);

        var item = response.Data.Items.Single();
        item.CompanyId.Should().Be(companyId);
        item.CompanyName.Should().Be("JOIN CRM");
        item.ModuleName.Should().Be("Support");
        item.IsActive.Should().BeTrue();
        item.CreatedAt.Should().Be(createdAt);

        context.Connection.LastCommandText.Should().Contain("cm.CompanyId = @CompanyId");
        context.Connection.LastCommandText.Should().Contain("cm.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("c.Name LIKE @CompanyName");
        context.Connection.LastCommandText.Should().Contain("sm.Name LIKE @ModuleName");
        context.Connection.LastCommandText.Should().Contain("cm.Created >= @CreatedFrom");
        context.Connection.LastCommandText.Should().Contain("cm.Created < @CreatedToExclusive");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");

        context.Connection.CapturedParameters["CompanyId"].Should().Be(companyId);
        context.Connection.CapturedParameters["CompanyName"].Should().Be("%JOIN%");
        context.Connection.CapturedParameters["ModuleName"].Should().Be("%Support%");
        context.Connection.CapturedParameters["CreatedFrom"].Should().Be(createdFrom);
        context.Connection.CapturedParameters["CreatedToExclusive"].Should().Be(createdTo.AddDays(1));
        context.Connection.CapturedParameters["Offset"].Should().Be(20);
        context.Connection.CapturedParameters["PageSize"].Should().Be(20);
    }

    /// <summary>
    /// Verifies that an empty result set still produces a valid paged response.
    /// This covers the SQL Server pagination branch and default pagination fallback.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoModulesMatch_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new GetCompanyModulesQueryTestContext(useNpgsqlConnection: false);
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "CompanyId",
                "CompanyName",
                "ModuleId",
                "ModuleName",
                "IsActive",
                "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var query = new GetCompanyModulesQuery(
            CompanyId: companyId,
            PageNumber: -5,
            PageSize: 0,
            CompanyName: "missing");

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
        context.Connection.CapturedParameters["CompanyId"].Should().Be(companyId);
        context.Connection.CapturedParameters["Offset"].Should().Be(5);
        context.Connection.CapturedParameters["PageSize"].Should().Be(5);
    }

    /// <summary>
    /// Creates a fake result set containing a single company module row.
    /// </summary>
    private static FakeResultSet CreateCompanyModuleItemsResultSet(Guid companyId, DateTime createdAt)
    {
        return FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["CompanyId"] = companyId,
                ["CompanyName"] = "JOIN CRM",
                ["ModuleId"] = Guid.NewGuid(),
                ["ModuleName"] = "Support",
                ["IsActive"] = true,
                ["CreatedAt"] = createdAt
            });
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the company module query tests.
    /// </summary>
    private sealed class GetCompanyModulesQueryTestContext
    {
        public GetCompanyModulesQueryTestContext(bool useNpgsqlConnection)
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

        public GetCompanyModulesQueryHandler CreateHandler()
        {
            return new GetCompanyModulesQueryHandler(
                ConnectionFactoryMock.Object,
                PaginationOptions);
        }
    }
}
