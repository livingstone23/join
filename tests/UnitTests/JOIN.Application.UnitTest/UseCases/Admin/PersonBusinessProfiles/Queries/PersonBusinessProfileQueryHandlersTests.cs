using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonBusinessProfiles.Queries;

/// <summary>
/// Contains the unit tests for the person business profile Dapper query handlers.
/// </summary>
public sealed class PersonBusinessProfileQueryHandlersTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly string[] Columns =
        ["Id", "PersonId", "IndustryId", "IndustryName", "TaxRegimeId", "TaxRegimeName", "Website", "FoundationDate", "IsActive", "CreatedAt"];

    [Fact]
    public async Task GetById_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateByIdHandler().Handle(new GetPersonBusinessProfileByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task GetById_WhenProfileIsMissing_ShouldReturnNotFound()
    {
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.Empty(Columns));

        var response = await context.CreateByIdHandler().Handle(new GetPersonBusinessProfileByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("PERSON_BUSINESS_PROFILE_NOT_FOUND");
    }

    [Fact]
    public async Task GetById_WhenProfileExists_ShouldReturnProfile()
    {
        var id = Guid.NewGuid();
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.FromRows(Row(id)));

        var response = await context.CreateByIdHandler().Handle(new GetPersonBusinessProfileByIdQuery(id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.IndustryName.Should().Be("Technology");
        response.Data.TaxRegimeName.Should().Be("General");
    }

    [Fact]
    public async Task GetByPersonId_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateByPersonHandler().Handle(new GetPersonBusinessProfilesByPersonIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task GetByPersonId_ShouldReturnRows()
    {
        var personId = Guid.NewGuid();
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.FromRows(Row(Guid.NewGuid()), Row(Guid.NewGuid())));

        var response = await context.CreateByPersonHandler().Handle(new GetPersonBusinessProfilesByPersonIdQuery(personId), CancellationToken.None);

        response.Data.Should().HaveCount(2);
        context.Connection.CapturedParameters["PersonId"].Should().Be(personId);
        context.Connection.CapturedParameters["CompanyId"].Should().Be(CompanyId);
    }

    private static Dictionary<string, object?> Row(Guid id) => new()
    {
        ["Id"] = id,
        ["PersonId"] = Guid.NewGuid(),
        ["IndustryId"] = Guid.NewGuid(),
        ["IndustryName"] = "Technology",
        ["TaxRegimeId"] = Guid.NewGuid(),
        ["TaxRegimeName"] = "General",
        ["Website"] = null,
        ["FoundationDate"] = null,
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

        public GetPersonBusinessProfileByIdQueryHandler CreateByIdHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);

        public GetPersonBusinessProfilesByPersonIdQueryHandler CreateByPersonHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
    }
}
