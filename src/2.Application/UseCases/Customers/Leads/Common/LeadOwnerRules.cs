using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Domain.Customers.Teams;
using JOIN.Domain.Security;

namespace JOIN.Application.UseCases.Customers.Leads.Common;

/// <summary>
/// Owner and team of a lead (SPEC 61 rule 4): the owner is an active member of the company; the team is the requested
/// one (a current membership of the owner in an active team), else the only current one, else the primary; none when
/// the owner has no current membership.
/// </summary>
public sealed class LeadOwnerRules(IUnitOfWork unitOfWork)
{
    public async Task<bool> IsActiveMemberAsync(Guid companyId, Guid userId)
    {
        var bound = (await unitOfWork.GetRepository<UserCompany>().GetAllIncludingDeletedAsync(uc =>
                uc.CompanyId == companyId && uc.UserId == userId && uc.GcRecord == 0))
            .Any();
        if (!bound)
        {
            return false;
        }

        var user = (await unitOfWork.GetRepository<ApplicationUser>().GetAllIncludingDeletedAsync(u => u.Id == userId)).FirstOrDefault();
        return user is { IsActive: true };
    }

    public async Task<(Guid? TeamId, Response<T>? Error)> ResolveTeamAsync<T>(Guid companyId, Guid ownerUserId, Guid? requestedTeamId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var memberships = (await unitOfWork.GetRepository<SalesTeamMember>().GetAllIncludingDeletedAsync(m =>
                m.CompanyId == companyId && m.UserId == ownerUserId && m.GcRecord == 0 && m.ValidFrom <= today && (m.ValidTo == null || m.ValidTo >= today)))
            .ToList();
        var teamIds = memberships.Select(m => m.SalesTeamId).ToList();
        var activeTeams = (await unitOfWork.GetRepository<SalesTeam>().GetAllIncludingDeletedAsync(t =>
                teamIds.Contains(t.Id) && t.GcRecord == 0 && t.IsActive))
            .Select(t => t.Id)
            .ToHashSet();
        var usable = memberships.Where(m => activeTeams.Contains(m.SalesTeamId)).ToList();

        if (requestedTeamId is { } requested)
        {
            return usable.Any(m => m.SalesTeamId == requested)
                ? (requested, null)
                : (null, Response<T>.Error(LeadErrorCodes.InvalidSalesTeam, ["The team is inactive or the owner is not a current member of it."]));
        }

        var chosen = usable.Count == 1
            ? usable[0]
            : usable.FirstOrDefault(m => m.IsPrimary) ?? usable.OrderBy(m => m.ValidFrom).FirstOrDefault();
        return (chosen?.SalesTeamId, null);
    }
}
