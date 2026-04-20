using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.EntityStatuses.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.EntityStatuses.Queries.GetEntityStatus;

/// <summary>
/// Contains the unit tests for the entity status listing query.
/// These tests verify company validation, search filters, pagination boundaries,
/// and the empty result branch using the shared fake Dapper infrastructure.
/// </summary>
public sealed class GetEntityStatusQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant company identifier is missing.
    /// This prevents the handler from executing any SQL without a valid company scope.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        // Arrange
        var context = new GetEntityStatusQueryTestContext(useNpgsqlConnection: false);
        var query = new GetEntityStatusQuery(Guid.Empty);
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
    /// Verifies the validation branch when the requested company does not exist.
    /// This ensures the query stops after the pre-check and does not execute the listing SQL.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ShouldReturnInvalidCompanyIdError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new GetEntityStatusQueryTestContext(useNpgsqlConnection: false);
        context.Connection.SetScalarResults(0);

        var query = new GetEntityStatusQuery(companyId);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        response.Errors.Should().Contain("The specified CompanyId does not exist.");
        context.Connection.LastCommandText.Should().Contain("SELECT COUNT(1)");
        context.Connection.CapturedParameters["CompanyId"].Should().Be(companyId);
    }

    /// <summary>
    /// Verifies that the handler applies the requested search filters and sanitized pagination values
    /// while returning the matching entity statuses.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFiltersAreProvided_ShouldApplyFiltersAndReturnPagedStatuses()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var createdFrom = DateTime.UtcNow.Date.AddDays(-30);
        var createdTo = DateTime.UtcNow.Date;
        var createdAt = DateTime.UtcNow;

        var context = new GetEntityStatusQueryTestContext(useNpgsqlConnection: true);
        context.Connection.SetScalarResults(1);
        context.Connection.SetResults(
            CreateEntityStatusItemsResultSet(createdAt),
            FakeResultSet.FromScalar(21));

        var query = new GetEntityStatusQuery(
            CompanyId: companyId,
            PageNumber: 0,
            PageSize: 100,
            Name: "  Open  ",
            ModuleName: "  ticket  ",
            CreatedFrom: createdFrom,
            CreatedTo: createdTo);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Entity statuses retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(2);
        response.Data.PageSize.Should().Be(20);
        response.Data.TotalCount.Should().Be(21);
        response.Data.TotalPages.Should().Be(2);

        var item = response.Data.Items.Single();
        item.Name.Should().Be("Open");
        item.Description.Should().Be("Default status for ticket workflows");
        item.Code.Should().Be(10);
        item.IsOperative.Should().BeTrue();
        item.CreatedAt.Should().Be(createdAt);

        context.Connection.LastCommandText.Should().Contain("WHERE es.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("es.Name LIKE @Name");
        context.Connection.LastCommandText.Should().Contain("COALESCE(es.Description, '') LIKE @ModuleName");
        context.Connection.LastCommandText.Should().Contain("es.Created >= @CreatedFrom");
        context.Connection.LastCommandText.Should().Contain("es.Created < @CreatedToExclusive");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");

        context.Connection.CapturedParameters["CompanyId"].Should().Be(companyId);
        context.Connection.CapturedParameters["Name"].Should().Be("%Open%");
        context.Connection.CapturedParameters["ModuleName"].Should().Be("%ticket%");
        context.Connection.CapturedParameters["CreatedFrom"].Should().Be(createdFrom);
        context.Connection.CapturedParameters["CreatedToExclusive"].Should().Be(createdTo.AddDays(1));
        context.Connection.CapturedParameters["Offset"].Should().Be(20);
        context.Connection.CapturedParameters["PageSize"].Should().Be(20);
    }

    /// <summary>
    /// Verifies that an empty result set still produces a valid paged response.
    /// This covers the SQL Server pagination branch and the empty collection path.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoStatusesMatch_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new GetEntityStatusQueryTestContext(useNpgsqlConnection: false);
        context.Connection.SetScalarResults(1);
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "Name",
                "Description",
                "Code",
                "IsOperative",
                "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var query = new GetEntityStatusQuery(
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
    /// Creates a fake result set containing a single entity status row.
    /// </summary>
    private static FakeResultSet CreateEntityStatusItemsResultSet(DateTime createdAt)
    {
        return FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["Name"] = "Open",
                ["Description"] = "Default status for ticket workflows",
                ["Code"] = 10,
                ["IsOperative"] = true,
                ["CreatedAt"] = createdAt
            });
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the entity status query tests.
    /// </summary>
    private sealed class GetEntityStatusQueryTestContext
    {
        public GetEntityStatusQueryTestContext(bool useNpgsqlConnection)
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

        public GetEntityStatusQueryHandler CreateHandler()
        {
            return new GetEntityStatusQueryHandler(
                ConnectionFactoryMock.Object,
                PaginationOptions);
        }
    }
}
