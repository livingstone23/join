using FluentAssertions;
using JOIN.Application.Exceptions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.PersonContacts.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonContacts.Queries;

/// <summary>
/// Contains the unit tests for the person contact Dapper query handlers.
/// </summary>
public sealed class PersonContactQueryHandlersTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly string[] Columns = ["Id", "PersonId", "ContactType", "ContactValue", "IsPrimary", "Comments", "CompanyId"];

    [Fact]
    public async Task GetById_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateByIdHandler().Handle(new GetPersonContactByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task GetById_WhenContactIsMissing_ShouldThrowNotFound()
    {
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.Empty(Columns));

        var act = () => context.CreateByIdHandler().Handle(new GetPersonContactByIdQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetById_WhenContactExists_ShouldReturnContact()
    {
        var id = Guid.NewGuid();
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.FromRows(Row(id)));

        var response = await context.CreateByIdHandler().Handle(new GetPersonContactByIdQuery(id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.ContactValue.Should().Be("ana@join.test");
        context.Connection.CapturedParameters["CompanyId"].Should().Be(CompanyId);
    }

    [Fact]
    public async Task GetByPersonId_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateByPersonHandler().Handle(new GetPersonContactsByPersonIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task GetByPersonId_ShouldReturnTenantScopedRows()
    {
        var personId = Guid.NewGuid();
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.FromRows(Row(Guid.NewGuid()), Row(Guid.NewGuid())));

        var response = await context.CreateByPersonHandler().Handle(new GetPersonContactsByPersonIdQuery(personId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().HaveCount(2);
        context.Connection.LastCommandText.Should().Contain("c.CompanyId = @CompanyId").And.Contain("c.GcRecord = 0");
        context.Connection.CapturedParameters["PersonId"].Should().Be(personId);
    }

    private static Dictionary<string, object?> Row(Guid id) => new()
    {
        ["Id"] = id,
        ["PersonId"] = Guid.NewGuid(),
        ["ContactType"] = 1,
        ["ContactValue"] = "ana@join.test",
        ["IsPrimary"] = true,
        ["Comments"] = null,
        ["CompanyId"] = CompanyId
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

        public GetPersonContactByIdQueryHandler CreateByIdHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);

        public GetPersonContactsByPersonIdQueryHandler CreateByPersonHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
    }
}
