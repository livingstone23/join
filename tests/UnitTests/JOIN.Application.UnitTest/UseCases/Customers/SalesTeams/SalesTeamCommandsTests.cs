using FluentAssertions;
using JOIN.Application.DTO.Customers.SalesTeams;
using JOIN.Application.Interface.Customers;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.SalesTeams.Members.Commands;
using JOIN.Application.UseCases.Customers.SalesTeams.TeamRoles.Commands;
using JOIN.Application.UseCases.Customers.SalesTeams.Teams.Commands;
using JOIN.Domain.Customers.Teams;
using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Application.UnitTest.UseCases.Customers.SalesTeams;

/// <summary>
/// SPEC 60: teams, management roles, memberships with one primary per user and one leader per team.
/// </summary>
public sealed class SalesTeamCommandsTests
{
    private sealed record Roles(TeamRoleDto Supervisor, TeamRoleDto Leader, TeamRoleDto Member);

    private static async Task<Roles> SeedRolesAsync(CustomerTestContext ctx) => new(
        (await ctx.SendAsync(new CreateTeamRoleCommand { Code = "SUPERVISOR", Name = "Supervisor comercial", LeadScope = 3, CanReassign = true })).Data!,
        (await ctx.SendAsync(new CreateTeamRoleCommand { Code = "LEADER", Name = "Líder", LeadScope = 2, IsLeader = true, CanReassign = true })).Data!,
        (await ctx.SendAsync(new CreateTeamRoleCommand { Code = "MEMBER", Name = "Integrante", LeadScope = 1 })).Data!);

    private static async Task<Guid> AddCompanyUserAsync(CustomerTestContext ctx, string name, bool bind = true, bool active = true)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), FirstName = name, LastName = "Test", Email = $"{name}@t.local", UserName = $"{name}@t.local", IsActive = active };
        ctx.Db.ApplicationUsers.Add(user);
        if (bind)
        {
            ctx.Db.UserCompanies.Add(new UserCompany { UserId = user.Id, CompanyId = ctx.CompanyId });
        }

        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        return user.Id;
    }

    private static async Task<SalesTeamDto> TeamAsync(CustomerTestContext ctx, string name) =>
        (await ctx.SendAsync(new CreateSalesTeamCommand { Name = name })).Data!;

    private static async Task<List<SalesTeamMember>> MembersAsync(CustomerTestContext ctx, Guid userId)
    {
        ctx.Db.ChangeTracker.Clear();
        return await ctx.Db.SalesTeamMembers.IgnoreQueryFilters().Where(m => m.UserId == userId).ToListAsync();
    }

    [Fact]
    public async Task Teams_ShouldHaveUniqueNames_AndCannotBeDeletedWithCurrentMembers()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var roles = await SeedRolesAsync(ctx);
        var team = await ctx.SendAsync(new CreateSalesTeamCommand { Name = " Equipo Capital ", Description = " Ventas " });

        team.Data!.Name.Should().Be("Equipo Capital");
        team.Data.Description.Should().Be("Ventas");
        (await ctx.SendAsync(new CreateSalesTeamCommand { Name = "equipo capital" })).Message.Should().Be(SalesTeamErrorCodes.DuplicateKey);
        var other = await TeamAsync(ctx, "Norte");
        (await ctx.SendAsync(new UpdateSalesTeamCommand { Id = other.Id, Name = "Equipo Capital" })).Message.Should().Be(SalesTeamErrorCodes.DuplicateKey);
        (await ctx.SendAsync(new UpdateSalesTeamCommand { Id = other.Id, Name = "Norte", IsActive = false })).Data!.IsActive.Should().BeFalse();
        (await ctx.SendAsync(new UpdateSalesTeamCommand { Id = Guid.NewGuid(), Name = "X" })).Message.Should().Be(SalesTeamErrorCodes.SalesTeamNotFound);

        var userId = await AddCompanyUserAsync(ctx, "ana");
        var member = (await ctx.SendAsync(new AddSalesTeamMemberCommand { SalesTeamId = team.Data.Id, UserId = userId, TeamRoleId = roles.Member.Id })).Data!;

        var inUse = await ctx.SendAsync(new DeleteSalesTeamCommand(team.Data.Id));
        inUse.Message.Should().Be(SalesTeamErrorCodes.SalesTeamInUse);
        inUse.Errors.Should().Equal("Active SalesTeamMembers: 1");
        (await ctx.SendAsync(new DeleteTeamRoleCommand(roles.Member.Id))).Message.Should().Be(SalesTeamErrorCodes.TeamRoleInUse);

        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        ctx.Db.ChangeTracker.Clear();
        var row = await ctx.Db.SalesTeamMembers.IgnoreQueryFilters().SingleAsync(m => m.Id == member.Id);
        row.ValidFrom = yesterday.AddDays(-5);
        row.ValidTo = yesterday;
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        (await ctx.SendAsync(new DeleteSalesTeamCommand(team.Data.Id))).IsSuccess.Should().BeTrue("history only");
        (await ctx.SendAsync(new DeleteTeamRoleCommand(roles.Member.Id))).IsSuccess.Should().BeTrue();
        (await ctx.SendAsync(new DeleteSalesTeamCommand(team.Data.Id))).Message.Should().Be(SalesTeamErrorCodes.SalesTeamNotFound);

        (await ctx.SendAsync(new RestoreSalesTeamCommand(team.Data.Id))).Message.Should().Be(SalesTeamErrorCodes.SuperAdminRequired);
        ctx.IsSuperAdmin = true;
        (await ctx.SendAsync(new RestoreSalesTeamCommand(team.Data.Id))).IsSuccess.Should().BeTrue();
        (await ctx.SendAsync(new RestoreTeamRoleCommand(roles.Member.Id))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Commands_WithoutCompany_ShouldFail()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        ctx.ActingCompanyId = Guid.Empty;

        (await ctx.SendAsync(new CreateSalesTeamCommand { Name = "A" })).Message.Should().Be(SalesTeamErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new UpdateSalesTeamCommand { Id = Guid.NewGuid(), Name = "A" })).Message.Should().Be(SalesTeamErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new DeleteSalesTeamCommand(Guid.NewGuid()))).Message.Should().Be(SalesTeamErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new RestoreSalesTeamCommand(Guid.NewGuid()))).Message.Should().Be(SalesTeamErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new AddSalesTeamMemberCommand { SalesTeamId = Guid.NewGuid() })).Message.Should().Be(SalesTeamErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new UpdateSalesTeamMemberCommand { SalesTeamId = Guid.NewGuid() })).Message.Should().Be(SalesTeamErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new CloseSalesTeamMemberCommand { SalesTeamId = Guid.NewGuid() })).Message.Should().Be(SalesTeamErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task AddMember_ShouldValidateTeamRoleUserAndDuplicates()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var roles = await SeedRolesAsync(ctx);
        var team = await TeamAsync(ctx, "Capital");
        var userId = await AddCompanyUserAsync(ctx, "ana");
        var outsider = await AddCompanyUserAsync(ctx, "out", bind: false);
        var inactive = await AddCompanyUserAsync(ctx, "off", active: false);
        await ctx.SendAsync(new UpdateTeamRoleCommand { Id = roles.Supervisor.Id, Code = "SUPERVISOR", Name = "Supervisor comercial", LeadScope = 3, IsActive = false });

        (await ctx.SendAsync(new AddSalesTeamMemberCommand { SalesTeamId = Guid.NewGuid(), UserId = userId, TeamRoleId = roles.Member.Id })).Message.Should().Be(SalesTeamErrorCodes.SalesTeamNotFound);
        (await ctx.SendAsync(new AddSalesTeamMemberCommand { SalesTeamId = team.Id, UserId = userId, TeamRoleId = roles.Supervisor.Id })).Message.Should().Be(SalesTeamErrorCodes.TeamRoleNotFound);
        (await ctx.SendAsync(new AddSalesTeamMemberCommand { SalesTeamId = team.Id, UserId = outsider, TeamRoleId = roles.Member.Id })).Message.Should().Be(SalesTeamErrorCodes.UserNotInCompany);
        (await ctx.SendAsync(new AddSalesTeamMemberCommand { SalesTeamId = team.Id, UserId = inactive, TeamRoleId = roles.Member.Id })).Message.Should().Be(SalesTeamErrorCodes.UserNotInCompany);

        var added = await ctx.SendAsync(new AddSalesTeamMemberCommand { SalesTeamId = team.Id, UserId = userId, TeamRoleId = roles.Member.Id });
        added.IsSuccess.Should().BeTrue(added.Message);
        added.Data!.IsPrimary.Should().BeTrue("first open membership");
        added.Data.IsCurrent.Should().BeTrue();
        added.Data.UserName.Should().Be("ana Test");
        added.Data.ValidFrom.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
        (await ctx.SendAsync(new AddSalesTeamMemberCommand { SalesTeamId = team.Id, UserId = userId, TeamRoleId = roles.Member.Id })).Message.Should().Be(SalesTeamErrorCodes.DuplicateKey);
    }

    [Fact]
    public async Task Primary_ShouldMoveWhenMarked_AndPassToTheOldestOpenWhenClosed()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var roles = await SeedRolesAsync(ctx);
        var a = await TeamAsync(ctx, "A");
        var b = await TeamAsync(ctx, "B");
        var c = await TeamAsync(ctx, "C");
        var userId = await AddCompanyUserAsync(ctx, "sup");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var inA = (await ctx.SendAsync(new AddSalesTeamMemberCommand { SalesTeamId = a.Id, UserId = userId, TeamRoleId = roles.Supervisor.Id, ValidFrom = today.AddDays(-30) })).Data!;
        var inB = (await ctx.SendAsync(new AddSalesTeamMemberCommand { SalesTeamId = b.Id, UserId = userId, TeamRoleId = roles.Supervisor.Id, ValidFrom = today.AddDays(-20) })).Data!;
        inB.IsPrimary.Should().BeFalse();
        var inC = (await ctx.SendAsync(new AddSalesTeamMemberCommand { SalesTeamId = c.Id, UserId = userId, TeamRoleId = roles.Member.Id, ValidFrom = today.AddDays(-10), IsPrimary = true })).Data!;
        inC.IsPrimary.Should().BeTrue();
        (await MembersAsync(ctx, userId)).Where(m => m.IsPrimary).Select(m => m.Id).Should().Equal(inC.Id);

        (await ctx.SendAsync(new UpdateSalesTeamMemberCommand { SalesTeamId = b.Id, MemberId = inB.Id, TeamRoleId = roles.Supervisor.Id, IsPrimary = true })).Data!.IsPrimary.Should().BeTrue();
        (await MembersAsync(ctx, userId)).Where(m => m.IsPrimary).Select(m => m.Id).Should().Equal(inB.Id);
        (await ctx.SendAsync(new UpdateSalesTeamMemberCommand { SalesTeamId = b.Id, MemberId = inB.Id, TeamRoleId = roles.Supervisor.Id, IsPrimary = false })).Data!.IsPrimary.Should().BeTrue("the flag moves only by marking another");

        (await ctx.SendAsync(new CloseSalesTeamMemberCommand { SalesTeamId = b.Id, MemberId = inB.Id, ValidTo = today.AddDays(-40) })).Message.Should().Be(SalesTeamErrorCodes.MemberDatesInvalid);
        var closed = await ctx.SendAsync(new CloseSalesTeamMemberCommand { SalesTeamId = b.Id, MemberId = inB.Id });
        closed.IsSuccess.Should().BeTrue(closed.Message);
        closed.Data!.ValidTo.Should().Be(today);
        closed.Data.IsPrimary.Should().BeFalse();
        (await MembersAsync(ctx, userId)).Where(m => m.IsPrimary).Select(m => m.Id).Should().Equal(inA.Id);

        (await ctx.SendAsync(new CloseSalesTeamMemberCommand { SalesTeamId = b.Id, MemberId = inB.Id })).Message.Should().Be(SalesTeamErrorCodes.SalesTeamMemberNotFound, "already closed");
        (await ctx.SendAsync(new UpdateSalesTeamMemberCommand { SalesTeamId = a.Id, MemberId = inB.Id, TeamRoleId = roles.Member.Id })).Message.Should().Be(SalesTeamErrorCodes.SalesTeamMemberNotFound);

        await ctx.SendAsync(new CloseSalesTeamMemberCommand { SalesTeamId = a.Id, MemberId = inA.Id });
        await ctx.SendAsync(new CloseSalesTeamMemberCommand { SalesTeamId = c.Id, MemberId = inC.Id });
        var all = await MembersAsync(ctx, userId);
        all.Should().HaveCount(3, "a leave never deletes rows");
        all.Should().OnlyContain(m => !m.IsPrimary && m.ValidTo != null);
    }

    [Fact]
    public async Task Leader_ShouldBeUniquePerTeam_ThePreviousOneBecomesMember()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var roles = await SeedRolesAsync(ctx);
        var team = await TeamAsync(ctx, "Capital");
        var first = await AddCompanyUserAsync(ctx, "lucia");
        var second = await AddCompanyUserAsync(ctx, "diego");

        var lucia = (await ctx.SendAsync(new AddSalesTeamMemberCommand { SalesTeamId = team.Id, UserId = first, TeamRoleId = roles.Leader.Id })).Data!;
        var diego = (await ctx.SendAsync(new AddSalesTeamMemberCommand { SalesTeamId = team.Id, UserId = second, TeamRoleId = roles.Member.Id })).Data!;
        lucia.IsLeader.Should().BeTrue();

        var promoted = await ctx.SendAsync(new UpdateSalesTeamMemberCommand { SalesTeamId = team.Id, MemberId = diego.Id, TeamRoleId = roles.Leader.Id });

        promoted.Data!.TeamRoleCode.Should().Be("LEADER");
        (await MembersAsync(ctx, first)).Single().TeamRoleId.Should().Be(roles.Member.Id);

        var third = await AddCompanyUserAsync(ctx, "carla");
        await ctx.SendAsync(new AddSalesTeamMemberCommand { SalesTeamId = team.Id, UserId = third, TeamRoleId = roles.Leader.Id });
        (await MembersAsync(ctx, second)).Single().TeamRoleId.Should().Be(roles.Member.Id);
    }

    [Fact]
    public async Task Leader_WithoutANonLeaderRole_ShouldFail()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var leader = (await ctx.SendAsync(new CreateTeamRoleCommand { Code = "LEADER", Name = "Líder", LeadScope = 2, IsLeader = true })).Data!;
        var team = await TeamAsync(ctx, "Solo");
        await ctx.SendAsync(new AddSalesTeamMemberCommand { SalesTeamId = team.Id, UserId = await AddCompanyUserAsync(ctx, "a"), TeamRoleId = leader.Id });

        var second = await ctx.SendAsync(new AddSalesTeamMemberCommand { SalesTeamId = team.Id, UserId = await AddCompanyUserAsync(ctx, "b"), TeamRoleId = leader.Id });

        second.Message.Should().Be(SalesTeamErrorCodes.TeamRoleNotFound);
    }

    [Fact]
    public async Task MembershipCloser_ShouldCloseEveryOpenMembershipOfTheUser()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var roles = await SeedRolesAsync(ctx);
        var a = await TeamAsync(ctx, "A");
        var b = await TeamAsync(ctx, "B");
        var userId = await AddCompanyUserAsync(ctx, "leaving");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await ctx.SendAsync(new AddSalesTeamMemberCommand { SalesTeamId = a.Id, UserId = userId, TeamRoleId = roles.Member.Id });
        await ctx.SendAsync(new AddSalesTeamMemberCommand { SalesTeamId = b.Id, UserId = userId, TeamRoleId = roles.Member.Id, ValidFrom = today.AddDays(5) });
        ctx.Db.ChangeTracker.Clear();

        var closed = await ctx.Get<ISalesTeamMembershipCloser>().CloseAllForUserAsync(ctx.CompanyId, userId);
        await ctx.UnitOfWork.SaveChangesAsync();

        closed.Should().Be(2);
        var rows = await MembersAsync(ctx, userId);
        rows.Single(m => m.SalesTeamId == a.Id).ValidTo.Should().Be(today);
        rows.Single(m => m.SalesTeamId == b.Id).ValidTo.Should().Be(today.AddDays(5), "never before ValidFrom");
        rows.Should().OnlyContain(m => !m.IsPrimary);
    }

    [Fact]
    public void Validators_ShouldRejectInvalidInput()
    {
        new CreateTeamRoleCommandValidator().Validate(new CreateTeamRoleCommand { Code = "A", Name = "A", LeadScope = 4 }).IsValid.Should().BeFalse();
        new UpdateTeamRoleCommandValidator().Validate(new UpdateTeamRoleCommand { Code = "A", Name = "A", LeadScope = 1 }).IsValid.Should().BeFalse("Id");
        new CreateSalesTeamCommandValidator().Validate(new CreateSalesTeamCommand { Name = "" }).IsValid.Should().BeFalse();
        new UpdateSalesTeamCommandValidator().Validate(new UpdateSalesTeamCommand { Name = "A" }).IsValid.Should().BeFalse("Id");
        new AddSalesTeamMemberCommandValidator().Validate(new AddSalesTeamMemberCommand()).Errors.Should().HaveCount(3);
        new UpdateSalesTeamMemberCommandValidator().Validate(new UpdateSalesTeamMemberCommand()).Errors.Should().HaveCount(3);
        new CloseSalesTeamMemberCommandValidator().Validate(new CloseSalesTeamMemberCommand()).Errors.Should().HaveCount(2);
    }
}
