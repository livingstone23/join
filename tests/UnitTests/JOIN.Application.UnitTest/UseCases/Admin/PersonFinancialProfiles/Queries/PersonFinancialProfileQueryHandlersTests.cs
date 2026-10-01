using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonFinancialProfiles.Queries;

/// <summary>
/// Contains the unit tests for the person financial profile Dapper query handlers.
/// </summary>
public sealed class PersonFinancialProfileQueryHandlersTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly string[] Columns =
        ["Id", "PersonId", "IncomeRangeId", "IncomeRangeName", "SourceOfFunds", "DeclaredDate", "IsCurrent", "IsActive", "CreatedAt"];

    [Fact]
    public async Task GetById_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateByIdHandler().Handle(new GetPersonFinancialProfileByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task GetById_WhenProfileIsMissing_ShouldReturnNotFound()
    {
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.Empty(Columns));

        var response = await context.CreateByIdHandler().Handle(new GetPersonFinancialProfileByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("PERSON_FINANCIAL_PROFILE_NOT_FOUND");
    }

    [Fact]
    public async Task GetById_WhenProfileExists_ShouldReturnProfile()
    {
        var id = Guid.NewGuid();
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.FromRows(Row(id)));

        var response = await context.CreateByIdHandler().Handle(new GetPersonFinancialProfileByIdQuery(id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.IncomeRangeName.Should().Be("Low");
        context.Connection.CapturedParameters["CompanyId"].Should().Be(CompanyId);
    }

    [Fact]
    public async Task GetByPersonId_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateByPersonHandler().Handle(new GetPersonFinancialProfilesByPersonIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task GetByPersonId_ShouldReturnRows()
    {
        var personId = Guid.NewGuid();
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.FromRows(Row(Guid.NewGuid())));

        var response = await context.CreateByPersonHandler().Handle(new GetPersonFinancialProfilesByPersonIdQuery(personId), CancellationToken.None);

        response.Data.Should().ContainSingle();
        context.Connection.LastCommandText.Should().Contain("ORDER BY pfp.IsCurrent DESC");
        context.Connection.CapturedParameters["PersonId"].Should().Be(personId);
    }

    private static Dictionary<string, object?> Row(Guid id) => new()
    {
        ["Id"] = id,
        ["PersonId"] = Guid.NewGuid(),
        ["IncomeRangeId"] = Guid.NewGuid(),
        ["IncomeRangeName"] = "Low",
        ["SourceOfFunds"] = "Salary",
        ["DeclaredDate"] = new DateTime(2025, 1, 1),
        ["IsCurrent"] = true,
        ["IsActive"] = true,
        ["CreatedAt"] = DateTime.UtcNow
    };

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

        public GetPersonFinancialProfileByIdQueryHandler CreateByIdHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);

        public GetPersonFinancialProfilesByPersonIdQueryHandler CreateByPersonHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
    }
}
