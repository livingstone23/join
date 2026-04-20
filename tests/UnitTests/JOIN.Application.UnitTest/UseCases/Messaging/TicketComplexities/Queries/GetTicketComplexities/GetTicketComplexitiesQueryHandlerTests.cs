using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketComplexities.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketComplexities.Queries.GetTicketComplexities;

/// <summary>
/// Contains the unit tests for the tenant-scoped ticket complexity query.
/// These tests verify CompanyId protection, tenant restriction, and pagination behavior.
/// </summary>
public sealed class GetTicketComplexitiesQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the authenticated CompanyId is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = new GetTicketComplexitiesQueryTestContext(Guid.Empty, useNpgsqlConnection: false);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetTicketComplexitiesQuery(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        response.Errors.Should().Contain("The authenticated token must contain a valid CompanyId claim.");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Verifies that the tenant-scoped query applies filters and the PostgreSQL pagination clause.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFiltersAreProvided_ShouldApplyTenantFiltersAndReturnPagedResult()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var createdAt = DateTime.UtcNow;
        var timeUnitId = _fixture.Create<Guid>();
        var context = new GetTicketComplexitiesQueryTestContext(companyId, useNpgsqlConnection: true);

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = Guid.NewGuid(),
                    ["CompanyId"] = companyId,
                    ["CompanyName"] = "JOIN CRM",
                    ["Name"] = "Standard",
                    ["Description"] = "Tenant complexity",
                    ["Code"] = 10,
                    ["ResolutionTimeUnits"] = 2,
                    ["TimeUnitId"] = timeUnitId,
                    ["IsActive"] = true,
                    ["CreatedAt"] = createdAt
                }),
            FakeResultSet.FromScalar(6));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(
            new GetTicketComplexitiesQuery(PageNumber: 0, PageSize: 100, Name: "  Standard  ", IsActive: true),
            CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket complexities retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(2);
        response.Data.PageSize.Should().Be(20);
        response.Data.TotalCount.Should().Be(6);
        response.Data.TotalPages.Should().Be(1);

        context.Connection.LastCommandText.Should().Contain("WHERE tc.CompanyId = @TenantId AND tc.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("tc.Name LIKE @Name");
        context.Connection.LastCommandText.Should().Contain("tc.IsActive = @IsActive");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");

        context.Connection.CapturedParameters["TenantId"].Should().Be(companyId);
        context.Connection.CapturedParameters["Name"].Should().Be("%Standard%");
        context.Connection.CapturedParameters["IsActive"].Should().Be(true);
        context.Connection.CapturedParameters["Offset"].Should().Be(20);
        context.Connection.CapturedParameters["PageSize"].Should().Be(20);
    }

    /// <summary>
    /// Verifies that empty results still return a valid response object.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoItemsMatch_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new GetTicketComplexitiesQueryTestContext(companyId, useNpgsqlConnection: false);
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
        var response = await handler.Handle(new GetTicketComplexitiesQuery(PageNumber: -1, PageSize: 0), CancellationToken.None);

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
    private sealed class GetTicketComplexitiesQueryTestContext
    {
        public GetTicketComplexitiesQueryTestContext(Guid companyId, bool useNpgsqlConnection)
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

        public GetTicketComplexitiesQueryHandler CreateHandler()
        {
            return new GetTicketComplexitiesQueryHandler(
                ConnectionFactoryMock.Object,
                PaginationOptions,
                CurrentUserServiceMock.Object);
        }
    }
}
