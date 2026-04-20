using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.IdentificationTypes.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.IdentificationTypes.Queries.GetIdentificationTypes;

/// <summary>
/// Contains the unit tests for the identification type listing query handler.
/// These tests verify date-range validation, filter application, pagination sanitization,
/// and empty result handling using the shared Dapper fake infrastructure.
/// </summary>
public sealed class GetIdentificationTypesQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the validation branch when the created date range is invalid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCreatedRangeIsInvalid_ShouldReturnInvalidCreatedRangeError()
    {
        // Arrange
        var context = new GetIdentificationTypesQueryTestContext(useNpgsqlConnection: false);
        var query = new GetIdentificationTypesQuery(
            CreatedFrom: DateTime.UtcNow.Date,
            CreatedTo: DateTime.UtcNow.Date.AddDays(-1));

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
    /// Verifies that the handler applies the requested filters and sanitized pagination values.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFiltersAreProvided_ShouldApplyFiltersAndReturnPagedIdentificationTypes()
    {
        // Arrange
        var createdDay = new DateTime(2026, 4, 18);
        var createdAt = new DateTime(2026, 4, 18, 9, 30, 0, DateTimeKind.Utc);

        var context = new GetIdentificationTypesQueryTestContext(useNpgsqlConnection: true);
        context.Connection.SetResults(
            CreateIdentificationTypeItemsResultSet(createdAt),
            FakeResultSet.FromScalar(21));

        var query = new GetIdentificationTypesQuery(
            PageNumber: 0,
            PageSize: 100,
            Name: "  Passport  ",
            Created: createdDay,
            CreatedFrom: createdDay.AddDays(-7),
            CreatedTo: createdDay.AddDays(2));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Identification types retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(2);
        response.Data.PageSize.Should().Be(20);
        response.Data.TotalCount.Should().Be(21);
        response.Data.TotalPages.Should().Be(2);

        var item = response.Data.Items.Single();
        item.Name.Should().Be("Passport");
        item.IsActive.Should().BeTrue();
        item.CreatedAt.Should().Be(createdAt);

        context.Connection.LastCommandText.Should().Contain("WHERE it.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("it.Name LIKE @Name");
        context.Connection.LastCommandText.Should().Contain("it.Created >= @CreatedDayStart AND it.Created < @CreatedDayEnd");
        context.Connection.LastCommandText.Should().Contain("it.Created >= @CreatedFrom");
        context.Connection.LastCommandText.Should().Contain("it.Created < @CreatedToExclusive");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");

        context.Connection.CapturedParameters["Name"].Should().Be("%Passport%");
        context.Connection.CapturedParameters["CreatedDayStart"].Should().Be(createdDay);
        context.Connection.CapturedParameters["CreatedDayEnd"].Should().Be(createdDay.AddDays(1));
        context.Connection.CapturedParameters["CreatedFrom"].Should().Be(createdDay.AddDays(-7));
        context.Connection.CapturedParameters["CreatedToExclusive"].Should().Be(createdDay.AddDays(3));
        context.Connection.CapturedParameters["Offset"].Should().Be(20);
        context.Connection.CapturedParameters["PageSize"].Should().Be(20);
    }

    /// <summary>
    /// Verifies that an empty result set still produces a valid paged response.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoIdentificationTypesMatch_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var context = new GetIdentificationTypesQueryTestContext(useNpgsqlConnection: false);
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "Name",
                "IsActive",
                "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var query = new GetIdentificationTypesQuery(
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
        context.Connection.CapturedParameters["Offset"].Should().Be(5);
        context.Connection.CapturedParameters["PageSize"].Should().Be(5);
    }

    /// <summary>
    /// Creates a fake result set containing a single identification type row.
    /// </summary>
    private static FakeResultSet CreateIdentificationTypeItemsResultSet(DateTime createdAt)
    {
        return FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["Name"] = "Passport",
                ["IsActive"] = true,
                ["CreatedAt"] = createdAt
            });
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the identification type listing tests.
    /// </summary>
    private sealed class GetIdentificationTypesQueryTestContext
    {
        public GetIdentificationTypesQueryTestContext(bool useNpgsqlConnection)
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

        public GetIdentificationTypesQueryHandler CreateHandler()
        {
            return new GetIdentificationTypesQueryHandler(
                ConnectionFactoryMock.Object,
                PaginationOptions);
        }
    }
}
