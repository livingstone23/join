using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.IncomeRanges.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.IncomeRanges.Queries.GetIncomeRanges;

/// <summary>
/// Contains the unit tests for the paged income range listing query handler.
/// </summary>
public sealed class GetIncomeRangesQueryHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty, useNpgsql: false);

        var response = await context.CreateHandler().Handle(new GetIncomeRangesQuery(), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenFiltersAreProvided_ShouldApplyFiltersAndPostgresPagination()
    {
        var context = new TestContext(CompanyId, useNpgsql: true);
        context.Connection.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["CompanyId"] = CompanyId,
                ["CompanyName"] = "JOIN CRM",
                ["DisplayName"] = "Low",
                ["MinimumValue"] = 0m,
                ["MaximumValue"] = null,
                ["CurrencyCode"] = "USD",
                ["IsActive"] = true,
                ["DisplayOrder"] = 1,
                ["CreatedAt"] = DateTime.UtcNow
            }),
            FakeResultSet.FromScalar(11));

        var response = await context.CreateHandler().Handle(
            new GetIncomeRangesQuery(PageNumber: 1, PageSize: 10, DisplayName: " Lo ", CurrencyCode: " usd ", IsActive: false),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Items.Should().ContainSingle();
        response.Data.TotalPages.Should().Be(2);
        context.Connection.LastCommandText.Should()
            .Contain("ir.DisplayName LIKE @DisplayName").And.Contain("ir.CurrencyCode = @CurrencyCode")
            .And.Contain("ir.IsActive = @IsActive").And.Contain("LIMIT @PageSize OFFSET @Offset");
        context.Connection.CapturedParameters["CurrencyCode"].Should().Be("USD");
        context.Connection.CapturedParameters["DisplayName"].Should().Be("%Lo%");
    }

    [Fact]
    public async Task Handle_WhenNothingMatches_ShouldReturnEmptyPage()
    {
        var context = new TestContext(CompanyId, useNpgsql: false);
        context.Connection.SetResults(
            FakeResultSet.Empty("Id", "CompanyId", "CompanyName", "DisplayName", "MinimumValue", "MaximumValue", "CurrencyCode", "IsActive", "DisplayOrder", "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var response = await context.CreateHandler().Handle(new GetIncomeRangesQuery(), CancellationToken.None);

        response.Data!.Items.Should().BeEmpty();
        response.Data.TotalPages.Should().Be(0);
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");
    }

    private sealed class TestContext
    {
        public TestContext(Guid companyId, bool useNpgsql)
        {
            Connection = useNpgsql ? new FakeNpgsqlDbConnection() : new FakeDbConnection();
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; }

        public GetIncomeRangesQueryHandler CreateHandler()
            => new(
                ConnectionFactoryMock.Object,
                Options.Create(new PaginationSettings { DefaultPageNumber = 1, DefaultPageSize = 10, MaxPageSize = 50 }),
                CurrentUserServiceMock.Object);
    }
}
