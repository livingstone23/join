using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.IncomeRanges.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.IncomeRanges.Queries.GetIncomeRangeById;

/// <summary>
/// Contains the unit tests for the income range detail query handler.
/// </summary>
public sealed class GetIncomeRangeByIdQueryHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly string[] Columns =
        ["Id", "CompanyId", "CompanyName", "DisplayName", "MinimumValue", "MaximumValue", "CurrencyCode", "IsActive", "DisplayOrder", "CreatedAt"];

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateHandler().Handle(new GetIncomeRangeByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenRangeIsMissing_ShouldReturnNotFound()
    {
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.Empty(Columns));

        var response = await context.CreateHandler().Handle(new GetIncomeRangeByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("INCOME_RANGE_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenRangeExists_ShouldReturnDetail()
    {
        var id = Guid.NewGuid();
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["Id"] = id,
            ["CompanyId"] = CompanyId,
            ["CompanyName"] = "JOIN CRM",
            ["DisplayName"] = "Low",
            ["MinimumValue"] = 0m,
            ["MaximumValue"] = 1000m,
            ["CurrencyCode"] = "USD",
            ["IsActive"] = true,
            ["DisplayOrder"] = 1,
            ["CreatedAt"] = DateTime.UtcNow
        }));

        var response = await context.CreateHandler().Handle(new GetIncomeRangeByIdQuery(id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.DisplayName.Should().Be("Low");
        response.Data.MaximumValue.Should().Be(1000m);
        context.Connection.CapturedParameters["CompanyId"].Should().Be(CompanyId);
    }

    private sealed class TestContext
    {
        public TestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetIncomeRangeByIdQueryHandler CreateHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
    }
}
