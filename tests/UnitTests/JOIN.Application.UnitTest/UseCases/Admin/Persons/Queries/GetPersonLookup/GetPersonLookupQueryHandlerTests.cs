using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.Persons.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Persons.Queries.GetPersonLookup;

/// <summary>
/// SPEC 54 — <c>GET /Persons/lookup</c>: one query, term validation, limit clamping and LIKE escaping.
/// </summary>
public sealed class GetPersonLookupQueryHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static readonly string[] Columns =
    [
        "PersonId", "PersonType", "FullName", "CommercialName", "IdentificationTypeId", "IdentificationTypeName",
        "IdentificationNumber", "IsCustomer", "IsCustomerActive", "CustomerId", "CustomerCode", "CustomerStageId",
        "CustomerStageCode", "CustomerStageName"
    ];

    private sealed class Context
    {
        public Context(Guid companyId)
        {
            CurrentUser.SetupGet(x => x.CompanyId).Returns(companyId);
            Factory.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ICurrentUserService> CurrentUser { get; } = new();
        public Mock<ISqlConnectionFactory> Factory { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetPersonLookupQueryHandler Handler() => new(Factory.Object, CurrentUser.Object);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" c ")]
    public async Task Handle_WithAShortTerm_ShouldAnswerInvalidSearchTerm(string? term)
    {
        var ctx = new Context(CompanyId);

        var response = await ctx.Handler().Handle(new GetPersonLookupQuery(term), CancellationToken.None);

        response.Message.Should().Be("INVALID_SEARCH_TERM");
        ctx.Connection.LastCommandText.Should().BeEmpty("no query runs for an invalid term");
    }

    [Fact]
    public async Task Handle_WithoutCompany_ShouldAnswerCompanyRequired()
    {
        var ctx = new Context(Guid.Empty);

        (await ctx.Handler().Handle(new GetPersonLookupQuery("cano"), CancellationToken.None)).Message.Should().Be("COMPANY_REQUIRED");
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData(0, 1)]
    [InlineData(500, 50)]
    [InlineData(7, 7)]
    public async Task Handle_ShouldClampTheLimit(int? limit, int expected)
    {
        var ctx = new Context(CompanyId);
        ctx.Connection.SetResults(FakeResultSet.Empty(Columns));

        await ctx.Handler().Handle(new GetPersonLookupQuery("cano", limit), CancellationToken.None);

        ctx.Connection.CapturedParameters["Limit"].Should().Be(expected);
    }

    [Fact]
    public async Task Handle_ShouldRunOneQueryOverPersonsAndCustomers()
    {
        var ctx = new Context(CompanyId);
        var personId = Guid.NewGuid();
        ctx.Connection.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["PersonId"] = personId,
            ["PersonType"] = 1,
            ["FullName"] = "Livingstone Cano",
            ["CommercialName"] = null,
            ["IdentificationTypeId"] = Guid.NewGuid(),
            ["IdentificationTypeName"] = "Pasaporte",
            ["IdentificationNumber"] = "20548796",
            ["IsCustomer"] = true,
            ["IsCustomerActive"] = false,
            ["CustomerId"] = Guid.NewGuid(),
            ["CustomerCode"] = "C000000001",
            ["CustomerStageId"] = Guid.NewGuid(),
            ["CustomerStageCode"] = "CUSTOMER",
            ["CustomerStageName"] = "Cliente"
        }));

        var response = await ctx.Handler().Handle(new GetPersonLookupQuery("  Norma Osorio  "), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var item = response.Data!.Single();
        item.PersonId.Should().Be(personId);
        item.IsCustomer.Should().BeTrue();
        item.IsCustomerActive.Should().BeFalse();
        ctx.Connection.CapturedParameters["Term"].Should().Be("Norma Osorio");
        ctx.Connection.CapturedParameters["Pattern"].Should().Be("%Norma Osorio%");
        ctx.Connection.CapturedParameters["TenantId"].Should().Be(CompanyId);
        ctx.Connection.LastCommandText.Should()
            .Contain("LEFT JOIN customer.customers cust")
            .And.Contain("p.IsActive = TRUE")
            .And.Contain("CONCAT_WS(' '")
            .And.Contain("cust.CustomerCode ILIKE @Pattern")
            .And.Contain("LIMIT @Limit")
            .And.NotContain("DISTINCT");
    }

    [Theory]
    [InlineData("50%", "50\\%")]
    [InlineData("a_b", "a\\_b")]
    [InlineData("c:\\x", "c:\\\\x")]
    public void EscapeLikePattern_ShouldEscapeWildcards(string input, string expected)
    {
        GetPersonLookupQueryHandler.EscapeLikePattern(input).Should().Be(expected);
    }
}
