using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Customers;
using JOIN.Application.Services.Customers;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UnitTest.UseCases.Customers.Leads;
using JOIN.Application.UseCases.Customers.LeadReports;
using JOIN.Application.UseCases.Customers.Leads.Common;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Customers.LeadReports;

/// <summary>
/// SPEC 64: definitions (conversion, weighted pipeline, days to close), filters and scope, and the shape of each report.
/// </summary>
public sealed class LeadReportsTests
{
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<ICurrentUserService> _user = new();
    private readonly LeadTestContext.FakeScopes _scopes = new();

    public LeadReportsTests()
    {
        _user.SetupGet(x => x.CompanyId).Returns(() => _companyId);
        _user.SetupGet(x => x.UserId).Returns(() => _userId.ToString());
    }

    private LeadReportContext Context(FakeSqlConnectionFactory factory) => new(factory, _user.Object, new LeadAccessGuard(_scopes, _user.Object));

    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);

    [Fact]
    public void Definitions()
    {
        LeadReportMath.Conversion(1, 3).Should().Be(33);
        LeadReportMath.Conversion(2, 3).Should().Be(67);
        LeadReportMath.Conversion(5, 0).Should().Be(0);
        LeadReportMath.Weighted(1000m, 25).Should().Be(250m);
        LeadReportMath.Weighted(333.33m, 33).Should().Be(110m);
        LeadReportMath.Days(12.345).Should().Be(12.3m);
        LeadReportMath.Days(null).Should().BeNull();
    }

    [Fact]
    public async Task Filters_RejectBadPeriods_AndTeamsOfOtherCompanies()
    {
        var factory = new FakeSqlConnectionFactory();
        var handler = new GetLeadReportFunnelQueryHandler(factory, Context(factory));

        (await handler.Handle(new GetLeadReportFunnelQuery(To, From), CancellationToken.None)).Message.Should().Be(LeadReportContext.ReportPeriodInvalid);
        (await handler.Handle(new GetLeadReportFunnelQuery(From, From.AddDays(367)), CancellationToken.None)).Message.Should().Be(LeadReportContext.ReportPeriodInvalid);

        factory.SetResults(FakeResultSet.FromScalar(false));
        (await handler.Handle(new GetLeadReportFunnelQuery(From, To, Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(LeadReportContext.InvalidSalesTeam);

        _user.SetupGet(x => x.UserId).Returns((string?)null);
        (await handler.Handle(new GetLeadReportFunnelQuery(), CancellationToken.None)).Message.Should().Be(LeadErrorCodes.UserRequired);
        _user.SetupGet(x => x.CompanyId).Returns(Guid.Empty);
        (await handler.Handle(new GetLeadReportFunnelQuery(), CancellationToken.None)).Message.Should().Be(LeadErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task Scope_AMemberOnlySeesOwnFigures_WithoutTheInactiveRule()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.Empty("LeadStageId"));

        await new GetLeadReportFunnelQueryHandler(factory, Context(factory)).Handle(new GetLeadReportFunnelQuery(From, To), CancellationToken.None);

        factory.LastCommandText.Should().Contain("l.OwnerUserId = @ScopeUserId").And.NotContain("l.Status = 2 OR");
        factory.CapturedParameters["ScopeUserId"].Should().Be(_userId);
        factory.CapturedParameters["To"].Should().Be(new DateTime(2026, 11, 1));
    }

    [Fact]
    public async Task Scope_CompanyWide_HasNoScopeFilter_AndTeamAndOwnerFiltersApply()
    {
        _scopes.Set(new LeadAccessScope(_userId, true, [], [], true));
        var factory = new FakeSqlConnectionFactory();
        var owner = Guid.NewGuid();
        factory.SetResults(FakeResultSet.FromScalar(true), FakeResultSet.Empty("LeadStageId"));

        await new GetLeadReportFunnelQueryHandler(factory, Context(factory)).Handle(new GetLeadReportFunnelQuery(From, To, Guid.NewGuid(), owner), CancellationToken.None);

        factory.LastCommandText.Should().NotContain("@ScopeUserId").And.Contain("l.SalesTeamId = @SalesTeamId").And.Contain("l.OwnerUserId = @OwnerUserId");
        factory.CapturedParameters["OwnerUserId"].Should().Be(owner);
    }

    [Fact]
    public async Task Summary_CombinesTotalsAndTheFunnel()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?> { ["CreatedCount"] = 4, ["WonCount"] = 1, ["WonValueBase"] = 500m, ["AvgDays"] = 7.25, ["BaseCurrencyCode"] = "DOP" }),
            FakeResultSet.FromRows(
                new Dictionary<string, object?> { ["LeadStageId"] = Guid.NewGuid(), ["Code"] = "NEW", ["Probability"] = 10, ["Count"] = 2, ["ValueBase"] = 1000m },
                new Dictionary<string, object?> { ["LeadStageId"] = Guid.NewGuid(), ["Code"] = "PROPOSAL", ["Probability"] = 60, ["Count"] = 1, ["ValueBase"] = 500m }));

        var summary = (await new GetLeadReportSummaryQueryHandler(factory, Context(factory)).Handle(new GetLeadReportSummaryQuery(From, To), CancellationToken.None)).Data!;

        summary.OpenCount.Should().Be(3);
        summary.CreatedCount.Should().Be(4);
        summary.WonCount.Should().Be(1);
        summary.ConversionPercent.Should().Be(25);
        summary.PipelineValueBase.Should().Be(1500m);
        summary.WeightedPipelineValueBase.Should().Be(400m);
        summary.WonValueBase.Should().Be(500m);
        summary.AvgDaysToClose.Should().Be(7.3m);
        summary.BaseCurrencyCode.Should().Be("DOP");
    }

    [Fact]
    public async Task ByTeam_Ranking_Product_Source_ContactType_MapTheirRows()
    {
        var team = new FakeSqlConnectionFactory();
        team.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?> { ["SalesTeamId"] = null, ["SalesTeamName"] = "Sin grupo", ["CreatedCount"] = 2, ["WonCount"] = 1, ["WonValueBase"] = 10m, ["AvgDays"] = 3.0 }));
        var teams = (await new GetLeadReportByTeamQueryHandler(team, Context(team)).Handle(new GetLeadReportByTeamQuery(From, To), CancellationToken.None)).Data!;
        teams.Single().ConversionPercent.Should().Be(50);
        team.LastCommandText.Should().Contain("COALESCE(t.Name, 'Sin grupo')").And.Contain("GROUP BY l.SalesTeamId");

        var ranking = new FakeSqlConnectionFactory();
        ranking.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?> { ["UserId"] = _userId, ["UserName"] = "Ana", ["CreatedCount"] = 3, ["WonCount"] = 3, ["WonValueBase"] = 9m, ["AvgDays"] = null, ["TopProduct"] = "CRM" }));
        var users = (await new GetLeadReportRankingQueryHandler(ranking, Context(ranking)).Handle(new GetLeadReportRankingQuery(From, To), CancellationToken.None)).Data!;
        users.Single().ConversionPercent.Should().Be(100);
        users.Single().TopProduct.Should().Be("CRM");
        ranking.LastCommandText.Should().Contain("WITH period AS").And.Contain("lp.Status = 2").And.Contain("STRING_AGG(DISTINCT t.Name");

        var product = new FakeSqlConnectionFactory();
        product.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?> { ["ProductId"] = Guid.NewGuid(), ["ProductCode"] = "CRM", ["LeadsCount"] = 1, ["Quantity"] = 10m, ["ValueBase"] = 100m }));
        (await new GetLeadReportByProductQueryHandler(product, Context(product)).Handle(new GetLeadReportByProductQuery(From, To), CancellationToken.None)).Data!.Single().ProductCode.Should().Be("CRM");
        product.LastCommandText.Should().Contain("COALESCE(l.WonExchangeRate, l.ExchangeRate)");

        var source = new FakeSqlConnectionFactory();
        source.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?> { ["LeadSourceId"] = Guid.NewGuid(), ["SourceName"] = "Web", ["CreatedCount"] = 1, ["WonCount"] = 0 }));
        (await new GetLeadReportBySourceQueryHandler(source, Context(source)).Handle(new GetLeadReportBySourceQuery(From, To), CancellationToken.None)).Data!.Single().SourceName.Should().Be("Web");

        var contacts = new FakeSqlConnectionFactory();
        contacts.SetResults(FakeResultSet.FromScalar(2), FakeResultSet.FromRows(
            new Dictionary<string, object?> { ["ContactTypeCode"] = "CALL", ["ContactTypeName"] = "Llamada", ["Count"] = 3 },
            new Dictionary<string, object?> { ["ContactTypeCode"] = "EMAIL", ["ContactTypeName"] = "Correo", ["Count"] = 2 }));
        var byType = (await new GetLeadReportByContactTypeQueryHandler(contacts, Context(contacts)).Handle(new GetLeadReportByContactTypeQuery(From, To), CancellationToken.None)).Data!;
        byType.InteractionsCount.Should().Be(5);
        byType.AveragePerWonLead.Should().Be(2.5m);

        var none = new FakeSqlConnectionFactory();
        none.SetResults(FakeResultSet.FromScalar(0), FakeResultSet.Empty("ContactTypeCode"));
        (await new GetLeadReportByContactTypeQueryHandler(none, Context(none)).Handle(new GetLeadReportByContactTypeQuery(From, To), CancellationToken.None)).Data!.AveragePerWonLead.Should().Be(0);
    }

    [Fact]
    public async Task WonLeads_PagesOrExportsUpTo10000()
    {
        var paging = Options.Create(new PaginationSettings());
        var paged = new FakeSqlConnectionFactory();
        paged.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["Code"] = "LD-000001", ["ClosedAt"] = DateTime.UtcNow }), FakeResultSet.FromScalar(1));
        var page = (await new GetLeadReportWonLeadsQueryHandler(paged, Context(paged), paging).Handle(new GetLeadReportWonLeadsQuery(From, To, PageSize: 5), CancellationToken.None)).Data!;
        page.Items.Single().Code.Should().Be("LD-000001");
        page.PageSize.Should().Be(5);

        var export = new FakeSqlConnectionFactory();
        export.SetResults(FakeResultSet.Empty("Id"), FakeResultSet.FromScalar(0));
        var all = (await new GetLeadReportWonLeadsQueryHandler(export, Context(export), paging).Handle(new GetLeadReportWonLeadsQuery(From, To, Export: true), CancellationToken.None)).Data!;
        all.PageSize.Should().Be(GetLeadReportWonLeadsQueryHandler.ExportLimit);
        export.CapturedParameters["PageSize"].Should().Be(10_000);

        (await new GetLeadReportWonLeadsQueryHandler(export, Context(export), paging).Handle(new GetLeadReportWonLeadsQuery(To, From), CancellationToken.None))
            .Message.Should().Be(LeadReportContext.ReportPeriodInvalid);
    }

    [Fact]
    public async Task EveryReport_PropagatesTheFilterError()
    {
        var factory = new FakeSqlConnectionFactory();
        var bad = (From: To, To: From);
        (await new GetLeadReportSummaryQueryHandler(factory, Context(factory)).Handle(new GetLeadReportSummaryQuery(bad.From, bad.To), CancellationToken.None)).IsSuccess.Should().BeFalse();
        (await new GetLeadReportByTeamQueryHandler(factory, Context(factory)).Handle(new GetLeadReportByTeamQuery(bad.From, bad.To), CancellationToken.None)).IsSuccess.Should().BeFalse();
        (await new GetLeadReportRankingQueryHandler(factory, Context(factory)).Handle(new GetLeadReportRankingQuery(bad.From, bad.To), CancellationToken.None)).IsSuccess.Should().BeFalse();
        (await new GetLeadReportByProductQueryHandler(factory, Context(factory)).Handle(new GetLeadReportByProductQuery(bad.From, bad.To), CancellationToken.None)).IsSuccess.Should().BeFalse();
        (await new GetLeadReportBySourceQueryHandler(factory, Context(factory)).Handle(new GetLeadReportBySourceQuery(bad.From, bad.To), CancellationToken.None)).IsSuccess.Should().BeFalse();
        (await new GetLeadReportByContactTypeQueryHandler(factory, Context(factory)).Handle(new GetLeadReportByContactTypeQuery(bad.From, bad.To), CancellationToken.None)).IsSuccess.Should().BeFalse();
    }
}
