using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Common.Companies.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Companies.Queries.GetCompaniesPaged;

/// <summary>
/// Contains the unit tests for the paged companies query.
/// These tests verify search filtering, pagination sanitization, and empty results.
/// </summary>
public sealed class GetCompaniesPagedQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies that the handler applies the search filter and returns a paged result.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSearchTermIsProvided_ShouldApplyFilterAndReturnPagedResult()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new GetCompaniesPagedQueryTestContext(useNpgsqlConnection: true);

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = companyId,
                    ["Name"] = "JOIN CRM",
                    ["TaxId"] = "RUC-123",
                    ["IsActive"] = true
                }),
            FakeResultSet.FromScalar(51));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetCompaniesPagedQuery(PageNumber: 0, PageSize: 100, SearchTerm: "  join  "), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Companies retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(1);
        response.Data.PageSize.Should().Be(50);
        response.Data.TotalCount.Should().Be(51);
        response.Data.TotalPages.Should().Be(2);
        response.Data.Items.Single().Id.Should().Be(companyId);

        context.Connection.LastCommandText.Should().Contain("WHERE c.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("c.Name LIKE @SearchTerm OR c.TaxId LIKE @SearchTerm");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");
        context.Connection.CapturedParameters["SearchTerm"].Should().Be("%join%");
        context.Connection.CapturedParameters["PageSize"].Should().Be(50);
    }

    /// <summary>
    /// Verifies that empty results still return a valid paged response.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoItemsMatch_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var context = new GetCompaniesPagedQueryTestContext(useNpgsqlConnection: false);
        context.Connection.SetResults(
            FakeResultSet.Empty("Id", "Name", "TaxId", "IsActive"),
            FakeResultSet.FromScalar(0));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetCompaniesPagedQuery(PageNumber: -1, PageSize: 0, SearchTerm: null), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().BeEmpty();
        response.Data.PageNumber.Should().Be(1);
        response.Data.PageSize.Should().Be(10);
        response.Data.TotalCount.Should().Be(0);
        response.Data.TotalPages.Should().Be(0);
        context.Connection.LastCommandText.Should().Contain("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY");
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the paged query tests.
    /// </summary>
    private sealed class GetCompaniesPagedQueryTestContext
    {
        public GetCompaniesPagedQueryTestContext(bool useNpgsqlConnection)
        {
            Connection = useNpgsqlConnection ? new FakeNpgsqlDbConnection() : new FakeDbConnection();
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; }

        public GetCompaniesPagedQueryHandler CreateHandler()
        {
            return new GetCompaniesPagedQueryHandler(ConnectionFactoryMock.Object);
        }
    }
}
