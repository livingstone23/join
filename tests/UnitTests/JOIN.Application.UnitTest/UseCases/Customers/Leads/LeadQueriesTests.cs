using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Customers;
using JOIN.Application.Services.Customers;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Customers.Leads.Common;
using JOIN.Application.UseCases.Customers.Leads.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Customers.Leads;

/// <summary>
/// SPEC 61 queries: tenant, scope filter, visibility of the detail/timeline, board, catalogs and the amount source.
/// </summary>
public sealed class LeadQueriesTests
{
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<ICurrentUserService> _user = new();
    private readonly LeadTestContext.FakeScopes _scopes = new();
    private readonly IOptions<PaginationSettings> _paging = Options.Create(new PaginationSettings());

    public LeadQueriesTests()
    {
        _user.SetupGet(x => x.CompanyId).Returns(() => _companyId);
        _user.SetupGet(x => x.UserId).Returns(() => _userId.ToString());
    }

    private LeadAccessGuard Guard => new(_scopes, _user.Object);

    private static Dictionary<string, object?> ScopeRow(int status, Guid owner, Guid? team = null) =>
        new() { ["Status"] = status, ["OwnerUserId"] = owner, ["SalesTeamId"] = team };

    [Fact]
    public async Task Inbox_DefaultsToOpen_AppliesFiltersAndTheScope()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.Empty("Id"), FakeResultSet.FromScalar(0));
        var team = Guid.NewGuid();
        _scopes.Set(new LeadAccessScope(_userId, false, [team], [], false));

        var response = await new GetLeadsQueryHandler(factory, _paging, _user.Object, Guard).Handle(
            new GetLeadsQuery(Search: "pos", LeadStageId: Guid.NewGuid(), OwnerUserId: Guid.NewGuid(), SalesTeamId: team, LeadSourceId: Guid.NewGuid(),
                RelationshipTypeId: Guid.NewGuid(), FromDate: new DateOnly(2026, 10, 1), ToDate: new DateOnly(2026, 10, 31)),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message);
        factory.CapturedParameters["Status"].Should().Be(1);
        factory.CapturedParameters["ScopeUserId"].Should().Be(_userId);
        factory.CapturedParameters["Search"].Should().Be("%pos%");
        factory.LastCommandText.Should().Contain("l.Status = 2 OR l.OwnerUserId = @ScopeUserId OR l.SalesTeamId = ANY(")
            .And.Contain("l.Code ILIKE @Search").And.Contain("l.LeadStageId = @LeadStageId").And.Contain("l.Created < @ToDate")
            .And.Contain("ORDER BY l.LastInteractionAt DESC NULLS LAST");
    }

    [Fact]
    public async Task Inbox_ForACompanyWideScope_HasNoScopeFilter_AndStatusZeroListsAll()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.Empty("Id"), FakeResultSet.FromScalar(0));
        _user.Setup(x => x.IsInRole("SuperAdmin")).Returns(true);

        await new GetLeadsQueryHandler(factory, _paging, _user.Object, Guard).Handle(new GetLeadsQuery(Status: 0), CancellationToken.None);

        factory.LastCommandText.Should().NotContain("@ScopeUserId").And.NotContain("l.Status = @Status");
    }

    [Fact]
    public async Task Board_GroupsOpenStages_WithTheFirst20Cards()
    {
        var factory = new FakeSqlConnectionFactory();
        var stage = Guid.NewGuid();
        factory.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?> { ["LeadStageId"] = stage, ["Code"] = "NEW", ["Name"] = "Nuevo", ["Count"] = 1, ["ValueBase"] = 10m }),
            FakeResultSet.FromRows(new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["LeadStageId"] = stage, ["Code"] = "LD-000001" }));

        var response = await new GetLeadBoardQueryHandler(factory, _user.Object, Guard).Handle(new GetLeadBoardQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        response.Data!.Single().Cards.Single().Code.Should().Be("LD-000001");
        factory.LastCommandText.Should().Contain("s.Kind IN (1, 2)").And.Contain("x.CardRank <= 20").And.Contain("l.SalesTeamId = @SalesTeamId");
    }

    [Fact]
    public async Task Detail_OutOfScope_IsNotFound_Inactive_IsVisible()
    {
        var other = Guid.NewGuid();
        var hidden = new FakeSqlConnectionFactory();
        hidden.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["Status"] = 1, ["OwnerUserId"] = other }), FakeResultSet.Empty("Id"));
        (await new GetLeadByIdQueryHandler(hidden, _user.Object, Guard).Handle(new GetLeadByIdQuery(Guid.NewGuid()), CancellationToken.None))
            .Message.Should().Be(LeadErrorCodes.LeadNotFound);

        var inactive = new FakeSqlConnectionFactory();
        inactive.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["Status"] = 2, ["OwnerUserId"] = other, ["BaseCurrencyCode"] = "DOP" }),
            FakeResultSet.FromRows(new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["ProductCode"] = "CRM", ["LineTotal"] = 10m }));
        var detail = await new GetLeadByIdQueryHandler(inactive, _user.Object, Guard).Handle(new GetLeadByIdQuery(Guid.NewGuid()), CancellationToken.None);
        detail.Data!.Products.Single().ProductCode.Should().Be("CRM");
        detail.Data.BaseCurrencyCode.Should().Be("DOP");

        var missing = new FakeSqlConnectionFactory();
        missing.SetResults(FakeResultSet.Empty("Id"), FakeResultSet.Empty("Id"));
        (await new GetLeadByIdQueryHandler(missing, _user.Object, Guard).Handle(new GetLeadByIdQuery(Guid.NewGuid()), CancellationToken.None))
            .Message.Should().Be(LeadErrorCodes.LeadNotFound);
    }

    [Fact]
    public async Task Timeline_And_Interactions_CheckVisibilityFirst()
    {
        var hidden = new FakeSqlConnectionFactory();
        hidden.SetResults(FakeResultSet.FromRows(ScopeRow(1, Guid.NewGuid())));
        (await new GetLeadTimelineQueryHandler(hidden, _user.Object, Guard).Handle(new GetLeadTimelineQuery(Guid.NewGuid()), CancellationToken.None))
            .Message.Should().Be(LeadErrorCodes.LeadNotFound);

        var hidden2 = new FakeSqlConnectionFactory();
        hidden2.SetResults(FakeResultSet.Empty("Status"));
        (await new GetLeadInteractionsQueryHandler(hidden2, _user.Object, Guard).Handle(new GetLeadInteractionsQuery(Guid.NewGuid()), CancellationToken.None))
            .Message.Should().Be(LeadErrorCodes.LeadNotFound);

        var mine = new FakeSqlConnectionFactory();
        mine.SetResults(FakeResultSet.FromRows(ScopeRow(1, _userId)), FakeResultSet.FromRows(new Dictionary<string, object?> { ["Kind"] = "Stage", ["Id"] = Guid.NewGuid(), ["Title"] = " → NEW" }));
        var timeline = await new GetLeadTimelineQueryHandler(mine, _user.Object, Guard).Handle(new GetLeadTimelineQuery(Guid.NewGuid(), 9999), CancellationToken.None);
        timeline.Data!.Single().Kind.Should().Be("Stage");
        mine.CapturedParameters["Limit"].Should().Be(500);
        mine.LastCommandText.Should().Contain("UNION ALL").And.Contain("customer.leadownershiphistory").And.Contain("ORDER BY x.At DESC");

        var interactions = new FakeSqlConnectionFactory();
        interactions.SetResults(FakeResultSet.FromRows(ScopeRow(2, Guid.NewGuid())), FakeResultSet.FromRows(new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["Summary"] = "Hola" }));
        (await new GetLeadInteractionsQueryHandler(interactions, _user.Object, Guard).Handle(new GetLeadInteractionsQuery(Guid.NewGuid()), CancellationToken.None))
            .Data!.Single().Summary.Should().Be("Hola");
    }

    [Fact]
    public async Task Duplicates_And_Catalogs_ReadTheCompanyRows()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["Code"] = "LD-000001" }));
        var personId = Guid.NewGuid();
        (await new GetLeadDuplicatesQueryHandler(factory, _user.Object).Handle(new GetLeadDuplicatesQuery(personId), CancellationToken.None)).Data!.Single().Code.Should().Be("LD-000001");
        factory.CapturedParameters["PersonId"].Should().Be(personId);
        factory.LastCommandText.Should().Contain("l.Status = 1");

        var catalogs = new FakeSqlConnectionFactory();
        catalogs.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["Code"] = "NEW", ["Kind"] = 1 }),
            FakeResultSet.Empty("Id"), FakeResultSet.Empty("Id"), FakeResultSet.Empty("Id"), FakeResultSet.Empty("Id"),
            FakeResultSet.FromRows(new Dictionary<string, object?> { ["CurrencyId"] = Guid.NewGuid(), ["Code"] = "DOP", ["IsBase"] = true }));
        var data = (await new GetLeadCatalogsQueryHandler(catalogs, _user.Object).Handle(new GetLeadCatalogsQuery(), CancellationToken.None)).Data!;
        data.Stages.Single().Code.Should().Be("NEW");
        data.Currencies.Single().IsBase.Should().BeTrue();
        catalogs.LastCommandText.Should().Contain("finance.companycurrencies").And.Contain("ct.IsActive = TRUE");
    }

    [Fact]
    public async Task Queries_WithoutCompanyOrUser_ShouldFail()
    {
        var factory = new FakeSqlConnectionFactory();
        _user.SetupGet(x => x.UserId).Returns((string?)null);
        (await new GetLeadsQueryHandler(factory, _paging, _user.Object, Guard).Handle(new GetLeadsQuery(), CancellationToken.None)).Message.Should().Be(LeadErrorCodes.UserRequired);

        _user.SetupGet(x => x.CompanyId).Returns(Guid.Empty);
        (await new GetLeadsQueryHandler(factory, _paging, _user.Object, Guard).Handle(new GetLeadsQuery(), CancellationToken.None)).Message.Should().Be(LeadErrorCodes.CompanyRequired);
        (await new GetLeadBoardQueryHandler(factory, _user.Object, Guard).Handle(new GetLeadBoardQuery(), CancellationToken.None)).Message.Should().Be(LeadErrorCodes.CompanyRequired);
        (await new GetLeadByIdQueryHandler(factory, _user.Object, Guard).Handle(new GetLeadByIdQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(LeadErrorCodes.CompanyRequired);
        (await new GetLeadTimelineQueryHandler(factory, _user.Object, Guard).Handle(new GetLeadTimelineQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(LeadErrorCodes.CompanyRequired);
        (await new GetLeadInteractionsQueryHandler(factory, _user.Object, Guard).Handle(new GetLeadInteractionsQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(LeadErrorCodes.CompanyRequired);
        (await new GetLeadDuplicatesQueryHandler(factory, _user.Object).Handle(new GetLeadDuplicatesQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(LeadErrorCodes.CompanyRequired);
        (await new GetLeadCatalogsQueryHandler(factory, _user.Object).Handle(new GetLeadCatalogsQuery(), CancellationToken.None)).Message.Should().Be(LeadErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task AmountSource_ReportsLeadsWithValue()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.FromScalar(true));
        var source = new LeadsAmountSource(factory);

        source.Name.Should().Be("Leads");
        (await source.HasAmountsAsync(_companyId)).Should().BeTrue();
        factory.LastCommandText.Should().Contain("l.EstimatedValue > 0");
    }
}
