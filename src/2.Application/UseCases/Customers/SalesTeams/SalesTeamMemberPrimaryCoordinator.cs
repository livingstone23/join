using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Customers.Teams;

namespace JOIN.Application.UseCases.Customers.SalesTeams;

/// <summary>
/// Keeps one open primary membership per user and company (SPEC 60 B.4): the first open membership becomes primary,
/// marking another one moves the flag, and closing the primary passes it to the oldest open membership left. The
/// previous primary is saved before the new one is marked so the filtered unique index
/// <c>UX_SalesTeamMembers_Company_User_Primary</c> never sees two rows (the command is transactional).
/// </summary>
public sealed class SalesTeamMemberPrimaryCoordinator(IUnitOfWork unitOfWork)
{
    /// <summary>Sets <see cref="SalesTeamMember.IsPrimary"/> on a membership about to be inserted.</summary>
    public async Task PrepareNewAsync(SalesTeamMember member, bool requestedPrimary, CancellationToken cancellationToken)
    {
        var others = await OpenOthersAsync(member);
        if (others.Count == 0)
        {
            member.IsPrimary = true;
            return;
        }

        member.IsPrimary = requestedPrimary;
        if (requestedPrimary)
        {
            await ClearAsync(others, cancellationToken);
        }
    }

    /// <summary>Makes <paramref name="member"/> (tracked, open) the user's primary membership.</summary>
    public async Task MakePrimaryAsync(SalesTeamMember member, CancellationToken cancellationToken)
    {
        if (member.IsPrimary)
        {
            return;
        }

        await ClearAsync(await OpenOthersAsync(member), cancellationToken);
        member.IsPrimary = true;
    }

    /// <summary>
    /// After closing <paramref name="member"/>: when it was the primary, the oldest open membership left (by
    /// <c>ValidFrom</c>, then creation) becomes primary; with none left the user has no team.
    /// </summary>
    public async Task OnClosedAsync(SalesTeamMember member, CancellationToken cancellationToken)
    {
        if (!member.IsPrimary)
        {
            return;
        }

        member.IsPrimary = false;
        var next = (await OpenOthersAsync(member)).OrderBy(m => m.ValidFrom).ThenBy(m => m.Created).FirstOrDefault();
        if (next is null)
        {
            return;
        }

        var repository = unitOfWork.GetRepository<SalesTeamMember>();
        await repository.UpdateAsync(member);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var tracked = await repository.GetAsync(next.Id);
        tracked!.IsPrimary = true;
        await repository.UpdateAsync(tracked);
    }

    private async Task<List<SalesTeamMember>> OpenOthersAsync(SalesTeamMember member) =>
        (await unitOfWork.GetRepository<SalesTeamMember>().GetAllIncludingDeletedAsync(m =>
            m.CompanyId == member.CompanyId && m.UserId == member.UserId && m.Id != member.Id && m.GcRecord == 0 && m.ValidTo == null))
        .ToList();

    private async Task ClearAsync(IEnumerable<SalesTeamMember> others, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.GetRepository<SalesTeamMember>();
        var changed = false;
        foreach (var other in others.Where(o => o.IsPrimary))
        {
            var tracked = await repository.GetAsync(other.Id);
            tracked!.IsPrimary = false;
            await repository.UpdateAsync(tracked);
            changed = true;
        }

        if (changed)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
