using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Domain.Customers.Teams;

namespace JOIN.Application.UseCases.Customers.SalesTeams;

/// <summary>
/// One current leader per team (SPEC 60 B.5): giving a leader role to a member moves the previous leader to the
/// company's <c>MEMBER</c> role (or, without it, the first active non-leader role), in the same transaction.
/// </summary>
public sealed class SalesTeamLeaderCoordinator(IUnitOfWork unitOfWork)
{
    /// <summary>Demotes the other open leaders of the team when <paramref name="role"/> is a leader role.</summary>
    public async Task<Response<T>?> ApplyAsync<T>(SalesTeamMember member, TeamRole role)
    {
        if (!role.IsLeader)
        {
            return null;
        }

        var leaderRoleIds = (await unitOfWork.GetRepository<TeamRole>().GetAllIncludingDeletedAsync(r =>
                r.CompanyId == member.CompanyId && r.IsLeader))
            .Select(r => r.Id)
            .ToList();
        var repository = unitOfWork.GetRepository<SalesTeamMember>();
        var leaders = (await repository.GetAllIncludingDeletedAsync(m =>
                m.SalesTeamId == member.SalesTeamId && m.Id != member.Id && m.GcRecord == 0 && m.ValidTo == null && leaderRoleIds.Contains(m.TeamRoleId)))
            .ToList();
        if (leaders.Count == 0)
        {
            return null;
        }

        var fallback = await MemberRoleAsync(member.CompanyId);
        if (fallback is null)
        {
            return Response<T>.Error(SalesTeamErrorCodes.TeamRoleNotFound, ["The company has no active non-leader role for the previous leader."]);
        }

        foreach (var leader in leaders)
        {
            var tracked = await repository.GetAsync(leader.Id);
            tracked!.TeamRoleId = fallback.Id;
            await repository.UpdateAsync(tracked);
        }

        return null;
    }

    private async Task<TeamRole?> MemberRoleAsync(Guid companyId)
    {
        var roles = (await unitOfWork.GetRepository<TeamRole>().GetAllIncludingDeletedAsync(r =>
                r.CompanyId == companyId && r.GcRecord == 0 && r.IsActive && !r.IsLeader))
            .ToList();
        return roles.FirstOrDefault(r => r.Code == TeamRole.Codes.Member)
            ?? roles.OrderBy(r => r.LeadScope).ThenBy(r => r.SortOrder).FirstOrDefault();
    }
}
