using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.Projects.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Projects.Queries.GetProjects;

/// <summary>
/// Contains the unit tests for the project listing query handler.
/// These tests verify tenant validation, SQL filtering, pagination sanitization,
/// and empty result handling.
/// </summary>
public sealed class GetProjectsQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant company identifier is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        // Arrange
        var context = new GetProjectsQueryHandlerTestContext(useNpgsqlConnection: false);
        var query = new GetProjectsQuery(Guid.Empty);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        response.Errors.Should().Contain("The X-Company-Id header is required.");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Verifies that the handler applies the provided filters and sanitized pagination values.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFiltersAreProvided_ShouldApplyFiltersAndReturnPagedProjects()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var entityStatusId = _fixture.Create<Guid>();
        var createdAt = new DateTime(2026, 4, 18, 11, 0, 0, DateTimeKind.Utc);

        var context = new GetProjectsQueryHandlerTestContext(useNpgsqlConnection: true);
        context.Connection.SetResults(
            CreateProjectItemsResultSet(companyId, entityStatusId, createdAt),
            FakeResultSet.FromScalar(21));

        var query = new GetProjectsQuery(
            CompanyId: companyId,
            PageNumber: 0,
            PageSize: 100,
            Name: "  CRM Core  ",
            EntityStatusId: entityStatusId);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Projects retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(2);
        response.Data.PageSize.Should().Be(20);
        response.Data.TotalCount.Should().Be(21);
        response.Data.TotalPages.Should().Be(2);

        var item = response.Data.Items.Single();
        item.CompanyId.Should().Be(companyId);
        item.CompanyName.Should().Be("JOIN CRM");
        item.Name.Should().Be("CRM Core");
        item.EntityStatusId.Should().Be(entityStatusId);
        item.EntityStatusName.Should().Be("Active");
        item.CreatedAt.Should().Be(createdAt);

        context.Connection.LastCommandText.Should().Contain("WHERE p.CompanyId = @CompanyId AND p.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("p.Name LIKE @Name");
        context.Connection.LastCommandText.Should().Contain("p.EntityStatusId = @EntityStatusId");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");

        context.Connection.CapturedParameters["CompanyId"].Should().Be(companyId);
        context.Connection.CapturedParameters["Name"].Should().Be("%CRM Core%");
        context.Connection.CapturedParameters["EntityStatusId"].Should().Be(entityStatusId);
        context.Connection.CapturedParameters["Offset"].Should().Be(20);
        context.Connection.CapturedParameters["PageSize"].Should().Be(20);
    }

    /// <summary>
    /// Verifies that an empty result set still produces a valid paged response.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoProjectsMatch_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new GetProjectsQueryHandlerTestContext(useNpgsqlConnection: false);
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "CompanyId",
                "CompanyName",
                "Name",
                "EntityStatusId",
                "EntityStatusName",
                "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var query = new GetProjectsQuery(
            CompanyId: companyId,
            PageNumber: -3,
            PageSize: 0,
            Name: "missing");

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
    /// Creates a fake result set containing a single project row.
    /// </summary>
    private static FakeResultSet CreateProjectItemsResultSet(Guid companyId, Guid entityStatusId, DateTime createdAt)
    {
        return FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["CompanyId"] = companyId,
                ["CompanyName"] = "JOIN CRM",
                ["Name"] = "CRM Core",
                ["EntityStatusId"] = entityStatusId,
                ["EntityStatusName"] = "Active",
                ["CreatedAt"] = createdAt
            });
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the project listing tests.
    /// </summary>
    private sealed class GetProjectsQueryHandlerTestContext
    {
        public GetProjectsQueryHandlerTestContext(bool useNpgsqlConnection)
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

        public GetProjectsQueryHandler CreateHandler()
        {
            return new GetProjectsQueryHandler(
                ConnectionFactoryMock.Object,
                PaginationOptions);
        }
    }
}
