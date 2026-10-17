using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Services.Customers;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Customers.SalesTeams.TeamRoles.Queries;
using JOIN.Application.UseCases.Customers.SalesTeams.Teams.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Customers.SalesTeams;

/// <summary>
/// SPEC 60 queries and the lead access scope resolver.
/// </summary>
public sealed class SalesTeamQueriesTests
{
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<ICurrentUserService> _user = new();

    public SalesTeamQueriesTests()
    {
        _user.SetupGet(x => x.CompanyId).Returns(() => _companyId);
        _user.SetupGet(x => x.UserId).Returns(() => _userId.ToString());
    }

    private static Dictionary<string, object?> Row(Guid team, int scope, bool reassign) =>
        new() { ["SalesTeamId"] = team, ["LeadScope"] = scope, ["CanReassign"] = reassign };

    [Fact]
    public async Task Resolver_Member_SeesOnlyOwnLeads()
    {
        var factory = new FakeSqlConnectionFactory();
        var team = Guid.NewGuid();
        factory.SetResults(FakeResultSet.FromRows(Row(team, 1, false)));

        var scope = await new LeadAccessScopeResolver(factory).ResolveAsync(_companyId, _userId);

        scope.IsCompanyWide.Should().BeFalse();
        scope.TeamIds.Should().BeEmpty();
        scope.Covers(_userId, team).Should().BeTrue();
        scope.Covers(Guid.NewGuid(), team).Should().BeFalse();
        scope.CanReassign(team).Should().BeFalse();
        factory.CapturedParameters["UserId"].Should().Be(_userId);
        factory.LastCommandText.Should().Contain("m.ValidFrom <= @Today").And.Contain("m.ValidTo IS NULL OR m.ValidTo >= @Today");
    }

    [Fact]
    public async Task Resolver_MemberInAAndLeaderInB_SeesTeamBAndOwnLeads()
    {
        var factory = new FakeSqlConnectionFactory();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        factory.SetResults(FakeResultSet.FromRows(Row(a, 1, false), Row(b, 2, true)));

        var scope = await new LeadAccessScopeResolver(factory).ResolveAsync(_companyId, _userId);

        scope.TeamIds.Should().Equal(b);
        scope.Covers(Guid.NewGuid(), b).Should().BeTrue();
        scope.Covers(Guid.NewGuid(), a).Should().BeFalse();
        scope.Covers(Guid.NewGuid(), null).Should().BeFalse();
        scope.CanReassign(b).Should().BeTrue();
        scope.CanReassign(a).Should().BeFalse();
    }

    [Fact]
    public async Task Resolver_CompanySupervisor_SeesEverything_AndNoMembershipsMeansOwnOnly()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.FromRows(Row(Guid.NewGuid(), 3, true), Row(Guid.NewGuid(), 3, true)));
        var supervisor = await new LeadAccessScopeResolver(factory).ResolveAsync(_companyId, _userId);

        supervisor.IsCompanyWide.Should().BeTrue();
        supervisor.CanReassignCompanyWide.Should().BeTrue();
        supervisor.Covers(Guid.NewGuid(), null).Should().BeTrue();
        supervisor.CanReassign(null).Should().BeTrue();

        var empty = new FakeSqlConnectionFactory();
        empty.SetResults(FakeResultSet.Empty("SalesTeamId", "LeadScope", "CanReassign"));
        var alone = await new LeadAccessScopeResolver(empty).ResolveAsync(_companyId, _userId);
        alone.IsCompanyWide.Should().BeFalse();
        alone.TeamIds.Should().BeEmpty();
        alone.Covers(_userId, null).Should().BeTrue();
    }

    [Fact]
    public async Task Teams_ShouldScopeToTheCompany_WithCurrentCountAndLeader()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.Empty("Id", "Name"));

        var response = await new GetSalesTeamsQueryHandler(factory, _user.Object).Handle(new GetSalesTeamsQuery(IncludeInactive: false), CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message);
        factory.CapturedParameters["CompanyId"].Should().Be(_companyId);
        factory.LastCommandText.Should().Contain("CurrentMembersCount").And.Contain("r.IsLeader = TRUE").And.Contain("t.GcRecord = 0").And.Contain("t.IsActive = TRUE");
    }

    [Fact]
    public async Task TeamById_ShouldReturnNotFound_OrTheTeamWithItsMembers()
    {
        var missing = new FakeSqlConnectionFactory();
        missing.SetResults(FakeResultSet.Empty("Id"));
        (await new GetSalesTeamByIdQueryHandler(missing, _user.Object).Handle(new GetSalesTeamByIdQuery(Guid.NewGuid()), CancellationToken.None))
            .Message.Should().Be(SalesTeamErrorCodes.SalesTeamNotFound);

        var factory = new FakeSqlConnectionFactory();
        var teamId = Guid.NewGuid();
        factory.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?> { ["Id"] = teamId, ["Name"] = "Capital", ["CurrentMembersCount"] = 1 }),
            FakeResultSet.FromRows(new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["SalesTeamId"] = teamId, ["UserName"] = "Ana", ["IsCurrent"] = true }));

        var response = await new GetSalesTeamByIdQueryHandler(factory, _user.Object).Handle(new GetSalesTeamByIdQuery(teamId), CancellationToken.None);

        response.Data!.Name.Should().Be("Capital");
        response.Data.Members.Single().UserName.Should().Be("Ana");
    }

    [Fact]
    public async Task Members_And_Me_ShouldFilterCurrentMemberships()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.Empty("Id"), FakeResultSet.Empty("Id"));

        (await new GetSalesTeamMembersQueryHandler(factory, _user.Object).Handle(new GetSalesTeamMembersQuery(Guid.NewGuid(), CurrentOnly: true), CancellationToken.None)).IsSuccess.Should().BeTrue();
        factory.LastCommandText.Should().Contain("AND m.ValidFrom <= @Today");
        (await new GetMySalesTeamsQueryHandler(factory, _user.Object).Handle(new GetMySalesTeamsQuery(), CancellationToken.None)).IsSuccess.Should().BeTrue();
        factory.CapturedParameters["UserId"].Should().Be(_userId);
        factory.LastCommandText.Should().Contain("m.UserId = @UserId").And.Contain("ORDER BY m.IsPrimary DESC");
    }

    [Fact]
    public async Task Queries_WithoutCompanyOrUser_ShouldFail()
    {
        var factory = new FakeSqlConnectionFactory();
        _user.SetupGet(x => x.UserId).Returns((string?)null);
        (await new GetMySalesTeamsQueryHandler(factory, _user.Object).Handle(new GetMySalesTeamsQuery(), CancellationToken.None)).Message.Should().Be(SalesTeamErrorCodes.UserRequired);

        _user.SetupGet(x => x.CompanyId).Returns(Guid.Empty);
        (await new GetSalesTeamsQueryHandler(factory, _user.Object).Handle(new GetSalesTeamsQuery(), CancellationToken.None)).Message.Should().Be(SalesTeamErrorCodes.CompanyRequired);
        (await new GetSalesTeamByIdQueryHandler(factory, _user.Object).Handle(new GetSalesTeamByIdQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(SalesTeamErrorCodes.CompanyRequired);
        (await new GetSalesTeamMembersQueryHandler(factory, _user.Object).Handle(new GetSalesTeamMembersQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(SalesTeamErrorCodes.CompanyRequired);
        (await new GetMySalesTeamsQueryHandler(factory, _user.Object).Handle(new GetMySalesTeamsQuery(), CancellationToken.None)).Message.Should().Be(SalesTeamErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task TeamRoles_ShouldReadTheirTable()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.Empty("Id"), FakeResultSet.Empty("Id"));

        (await new GetTeamRolesQueryHandler(factory, _user.Object).Handle(new GetTeamRolesQuery(), CancellationToken.None)).IsSuccess.Should().BeTrue();
        factory.LastCommandText.Should().Contain("customer.teamroles").And.Contain("t.LeadScope, t.IsLeader, t.CanReassign");
        (await new GetTeamRoleByIdQueryHandler(factory, _user.Object).Handle(new GetTeamRoleByIdQuery(Guid.NewGuid()), CancellationToken.None))
            .Message.Should().Be(SalesTeamErrorCodes.TeamRoleNotFound);
    }
}
