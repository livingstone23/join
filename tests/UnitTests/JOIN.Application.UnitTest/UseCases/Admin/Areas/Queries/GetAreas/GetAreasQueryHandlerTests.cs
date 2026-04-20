using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.Areas.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Areas.Queries.GetAreas;

/// <summary>
/// Contains the unit tests for the area listing query handler.
/// These tests verify tenant validation, created-range guards, SQL filtering,
/// pagination sanitization, and empty result handling.
/// </summary>
public sealed class GetAreasQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant company identifier is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        // Arrange
        var context = new GetAreasQueryHandlerTestContext(useNpgsqlConnection: false);
        var query = new GetAreasQuery(Guid.Empty);
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
    /// Verifies the validation branch when the created date range is invalid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCreatedRangeIsInvalid_ShouldReturnInvalidCreatedRangeError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var query = new GetAreasQuery(
            CompanyId: companyId,
            CreatedFrom: DateTime.UtcNow.Date,
            CreatedTo: DateTime.UtcNow.Date.AddDays(-1));

        var context = new GetAreasQueryHandlerTestContext(useNpgsqlConnection: false);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_CREATED_RANGE");
        response.Errors.Should().Contain("CreatedFrom must be less than or equal to CreatedTo.");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Verifies that the handler applies the provided filters and sanitized pagination values.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFiltersAreProvided_ShouldApplyFiltersAndReturnPagedAreas()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var entityStatusId = _fixture.Create<Guid>();
        var createdFrom = new DateTime(2026, 4, 1);
        var createdTo = new DateTime(2026, 4, 20);
        var createdAt = new DateTime(2026, 4, 18, 10, 30, 0, DateTimeKind.Utc);

        var context = new GetAreasQueryHandlerTestContext(useNpgsqlConnection: true);
        context.Connection.SetResults(
            CreateAreaItemsResultSet(companyId, entityStatusId, createdAt),
            FakeResultSet.FromScalar(21));

        var query = new GetAreasQuery(
            CompanyId: companyId,
            PageNumber: 0,
            PageSize: 100,
            Name: "  Support  ",
            CreatedFrom: createdFrom,
            CreatedTo: createdTo);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Areas retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(2);
        response.Data.PageSize.Should().Be(20);
        response.Data.TotalCount.Should().Be(21);
        response.Data.TotalPages.Should().Be(2);

        var item = response.Data.Items.Single();
        item.CompanyId.Should().Be(companyId);
        item.CompanyName.Should().Be("JOIN CRM");
        item.Name.Should().Be("Support");
        item.EntityStatusId.Should().Be(entityStatusId);
        item.EntityStatusName.Should().Be("Active");
        item.Created.Should().Be(createdAt);

        context.Connection.LastCommandText.Should().Contain("WHERE a.CompanyId = @CompanyId AND a.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("a.Name LIKE @Name");
        context.Connection.LastCommandText.Should().Contain("a.Created >= @CreatedFrom");
        context.Connection.LastCommandText.Should().Contain("a.Created < @CreatedToExclusive");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");

        context.Connection.CapturedParameters["CompanyId"].Should().Be(companyId);
        context.Connection.CapturedParameters["Name"].Should().Be("%Support%");
        context.Connection.CapturedParameters["CreatedFrom"].Should().Be(createdFrom);
        context.Connection.CapturedParameters["CreatedToExclusive"].Should().Be(createdTo.AddDays(1));
        context.Connection.CapturedParameters["Offset"].Should().Be(20);
        context.Connection.CapturedParameters["PageSize"].Should().Be(20);
    }

    /// <summary>
    /// Verifies that an empty result set still produces a valid paged response.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoAreasMatch_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new GetAreasQueryHandlerTestContext(useNpgsqlConnection: false);
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "CompanyId",
                "CompanyName",
                "Name",
                "EntityStatusId",
                "EntityStatusName",
                "Created"),
            FakeResultSet.FromScalar(0));

        var query = new GetAreasQuery(
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
    /// Creates a fake result set containing a single area row.
    /// </summary>
    private static FakeResultSet CreateAreaItemsResultSet(Guid companyId, Guid entityStatusId, DateTime createdAt)
    {
        return FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["CompanyId"] = companyId,
                ["CompanyName"] = "JOIN CRM",
                ["Name"] = "Support",
                ["EntityStatusId"] = entityStatusId,
                ["EntityStatusName"] = "Active",
                ["Created"] = createdAt
            });
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the area listing tests.
    /// </summary>
    private sealed class GetAreasQueryHandlerTestContext
    {
        public GetAreasQueryHandlerTestContext(bool useNpgsqlConnection)
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

        public GetAreasQueryHandler CreateHandler()
        {
            return new GetAreasQueryHandler(
                ConnectionFactoryMock.Object,
                PaginationOptions);
        }
    }
}
