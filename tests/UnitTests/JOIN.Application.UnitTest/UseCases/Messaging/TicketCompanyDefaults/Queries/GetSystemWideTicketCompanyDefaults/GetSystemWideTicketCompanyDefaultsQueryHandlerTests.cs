using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketCompanyDefaults.Queries.GetSystemWideTicketCompanyDefaults;

/// <summary>
/// Contains the unit tests for the system-wide ticket company default listing query.
/// These tests verify filter generation, pagination sanitization, and empty result handling
/// using the shared fake Dapper infrastructure.
/// </summary>
public sealed class GetSystemWideTicketCompanyDefaultsQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies that all supported string filters are translated into SQL parameters
    /// and that pagination is sanitized correctly for the PostgreSQL branch.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFiltersAreProvided_ShouldApplyFiltersAndReturnPagedConfigurations()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var createdAt = DateTime.UtcNow;

        var context = new GetSystemWideTicketCompanyDefaultsQueryTestContext(useNpgsqlConnection: true);
        context.Connection.SetResults(
            CreateItemsResultSet(companyId, createdAt),
            FakeResultSet.FromScalar(21));

        var query = new GetSystemWideTicketCompanyDefaultsQuery(
            PageNumber: 0,
            PageSize: 100,
            CompanyName: "  JOIN  ",
            StartCode: "  TCK  ",
            TicketStatusDefaultName: "  Open  ",
            TicketComplexityDefaultName: "  High  ",
            TimeUnitDefaultName: "  Hours  ",
            AreaName: "  Support  ",
            ProjectName: "  CRM  ",
            ChannelName: "  Email  ");

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("System-wide ticket company default configurations retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(2);
        response.Data.PageSize.Should().Be(20);
        response.Data.TotalCount.Should().Be(21);
        response.Data.TotalPages.Should().Be(2);

        var item = response.Data.Items.Single();
        item.CompanyId.Should().Be(companyId);
        item.CompanyName.Should().Be("JOIN CRM");
        item.StartCode.Should().Be("TCK");
        item.StatusName.Should().Be("Open");
        item.ComplexityName.Should().Be("High");
        item.TimeUnitName.Should().Be("Hours");
        item.AreaName.Should().Be("Support");
        item.ProjectName.Should().Be("CRM Rollout");
        item.ChannelName.Should().Be("Email");

        context.Connection.LastCommandText.Should().Contain("c.Name LIKE @CompanyName");
        context.Connection.LastCommandText.Should().Contain("tcd.StartCode LIKE @StartCode");
        context.Connection.LastCommandText.Should().Contain("ts.Name LIKE @TicketStatusDefaultName");
        context.Connection.LastCommandText.Should().Contain("tc.Name LIKE @TicketComplexityDefaultName");
        context.Connection.LastCommandText.Should().Contain("tu.Name LIKE @TimeUnitDefaultName");
        context.Connection.LastCommandText.Should().Contain("a.Name LIKE @AreaName");
        context.Connection.LastCommandText.Should().Contain("p.Name LIKE @ProjectName");
        context.Connection.LastCommandText.Should().Contain("ch.Name LIKE @ChannelName");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");

        context.Connection.CapturedParameters["CompanyName"].Should().Be("%JOIN%");
        context.Connection.CapturedParameters["StartCode"].Should().Be("%TCK%");
        context.Connection.CapturedParameters["TicketStatusDefaultName"].Should().Be("%Open%");
        context.Connection.CapturedParameters["TicketComplexityDefaultName"].Should().Be("%High%");
        context.Connection.CapturedParameters["TimeUnitDefaultName"].Should().Be("%Hours%");
        context.Connection.CapturedParameters["AreaName"].Should().Be("%Support%");
        context.Connection.CapturedParameters["ProjectName"].Should().Be("%CRM%");
        context.Connection.CapturedParameters["ChannelName"].Should().Be("%Email%");
        context.Connection.CapturedParameters["Offset"].Should().Be(20);
        context.Connection.CapturedParameters["PageSize"].Should().Be(20);
    }

    /// <summary>
    /// Verifies that an empty result set still returns a valid paged payload
    /// while using the SQL Server pagination branch.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoConfigurationsMatch_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var context = new GetSystemWideTicketCompanyDefaultsQueryTestContext(useNpgsqlConnection: false);
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "CompanyId",
                "CompanyName",
                "GcRecord",
                "StartCode",
                "CodeSequenceLength",
                "UsePersonalizedCode",
                "TicketStatusDefaultId",
                "StatusName",
                "TicketComplexityDefaultId",
                "ComplexityName",
                "TimeUnitDefaultId",
                "TimeUnitName",
                "AreaDefaultId",
                "AreaName",
                "ProjectDefaultId",
                "ProjectName",
                "ChannelDefaultId",
                "ChannelName",
                "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var query = new GetSystemWideTicketCompanyDefaultsQuery(
            PageNumber: -10,
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
        context.Connection.CapturedParameters["Offset"].Should().Be(5);
        context.Connection.CapturedParameters["PageSize"].Should().Be(5);
    }

    /// <summary>
    /// Creates a fake result set containing one ticket company default configuration.
    /// </summary>
    private static FakeResultSet CreateItemsResultSet(Guid companyId, DateTime createdAt)
    {
        return FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["CompanyId"] = companyId,
                ["CompanyName"] = "JOIN CRM",
                ["GcRecord"] = 0,
                ["StartCode"] = "TCK",
                ["CodeSequenceLength"] = 6,
                ["UsePersonalizedCode"] = true,
                ["TicketStatusDefaultId"] = Guid.NewGuid(),
                ["StatusName"] = "Open",
                ["TicketComplexityDefaultId"] = Guid.NewGuid(),
                ["ComplexityName"] = "High",
                ["TimeUnitDefaultId"] = Guid.NewGuid(),
                ["TimeUnitName"] = "Hours",
                ["AreaDefaultId"] = Guid.NewGuid(),
                ["AreaName"] = "Support",
                ["ProjectDefaultId"] = Guid.NewGuid(),
                ["ProjectName"] = "CRM Rollout",
                ["ChannelDefaultId"] = Guid.NewGuid(),
                ["ChannelName"] = "Email",
                ["CreatedAt"] = createdAt
            });
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the query tests.
    /// </summary>
    private sealed class GetSystemWideTicketCompanyDefaultsQueryTestContext
    {
        public GetSystemWideTicketCompanyDefaultsQueryTestContext(bool useNpgsqlConnection)
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

        public GetSystemWideTicketCompanyDefaultsQueryHandler CreateHandler()
        {
            return new GetSystemWideTicketCompanyDefaultsQueryHandler(
                ConnectionFactoryMock.Object,
                PaginationOptions);
        }
    }
}
