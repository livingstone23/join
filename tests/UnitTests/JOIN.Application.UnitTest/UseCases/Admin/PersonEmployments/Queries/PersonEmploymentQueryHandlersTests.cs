using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.PersonEmployments.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonEmployments.Queries;

/// <summary>
/// Contains the unit tests for the person employment Dapper query handlers.
/// </summary>
public sealed class PersonEmploymentQueryHandlersTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly string[] Columns = ["Id", "PersonId", "EmployerName", "JobTitle", "StartDate", "EndDate", "IsCurrent", "IsActive", "CreatedAt"];

    [Fact]
    public async Task GetById_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateByIdHandler().Handle(new GetPersonEmploymentByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task GetById_WhenEmploymentIsMissing_ShouldReturnNotFound()
    {
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.Empty(Columns));

        var response = await context.CreateByIdHandler().Handle(new GetPersonEmploymentByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("PERSON_EMPLOYMENT_NOT_FOUND");
    }

    [Fact]
    public async Task GetById_WhenEmploymentExists_ShouldReturnEmployment()
    {
        var id = Guid.NewGuid();
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.FromRows(Row(id)));

        var response = await context.CreateByIdHandler().Handle(new GetPersonEmploymentByIdQuery(id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.EmployerName.Should().Be("JOIN");
        context.Connection.CapturedParameters["CompanyId"].Should().Be(CompanyId);
    }

    [Fact]
    public async Task GetByPersonId_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateByPersonHandler().Handle(new GetPersonEmploymentsByPersonIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task GetByPersonId_ShouldReturnRowsOrderedByCurrent()
    {
        var personId = Guid.NewGuid();
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.FromRows(Row(Guid.NewGuid()), Row(Guid.NewGuid())));

        var response = await context.CreateByPersonHandler().Handle(new GetPersonEmploymentsByPersonIdQuery(personId), CancellationToken.None);

        response.Data.Should().HaveCount(2);
        context.Connection.LastCommandText.Should().Contain("ORDER BY IsCurrent DESC");
        context.Connection.CapturedParameters["PersonId"].Should().Be(personId);
    }

    private static Dictionary<string, object?> Row(Guid id) => new()
    {
        ["Id"] = id,
        ["PersonId"] = Guid.NewGuid(),
        ["EmployerName"] = "JOIN",
        ["JobTitle"] = "Dev",
        ["StartDate"] = new DateTime(2024, 1, 1),
        ["EndDate"] = null,
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

        public GetPersonEmploymentByIdQueryHandler CreateByIdHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);

        public GetPersonEmploymentsByPersonIdQueryHandler CreateByPersonHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
    }
}
