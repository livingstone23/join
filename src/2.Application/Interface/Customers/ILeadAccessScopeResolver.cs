namespace JOIN.Application.Interface.Customers;

/// <summary>
/// The leads a user can see and reassign in a company (SPEC 60 rule 5): the union of what each current membership's
/// role gives, plus the user's own leads. SuperAdmin handling belongs to the caller (it sees everything).
/// </summary>
public interface ILeadAccessScopeResolver
{
    Task<LeadAccessScope> ResolveAsync(Guid companyId, Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolved lead scope of a user. <see cref="IsCompanyWide"/>: any user's leads; <see cref="TeamIds"/>: the leads of
/// those teams; always the user's own leads. Reassigning needs a <c>CanReassign</c> role on the lead's team
/// (<see cref="CanReassignTeamIds"/>) or company-wide (<see cref="CanReassignCompanyWide"/>).
/// </summary>
public sealed record LeadAccessScope(
    Guid UserId,
    bool IsCompanyWide,
    IReadOnlyList<Guid> TeamIds,
    IReadOnlyList<Guid> CanReassignTeamIds,
    bool CanReassignCompanyWide)
{
    /// <summary>Whether the scope covers a lead owned by <paramref name="ownerUserId"/> in <paramref name="salesTeamId"/>.</summary>
    public bool Covers(Guid ownerUserId, Guid? salesTeamId) =>
        IsCompanyWide || ownerUserId == UserId || (salesTeamId is { } teamId && TeamIds.Contains(teamId));

    /// <summary>Whether the user can reassign a lead of <paramref name="salesTeamId"/>.</summary>
    public bool CanReassign(Guid? salesTeamId) =>
        CanReassignCompanyWide || (salesTeamId is { } teamId && CanReassignTeamIds.Contains(teamId));
}

/// <summary>
/// Closes the open sales team memberships of a user who leaves a company (SPEC 60 rule 6), in the caller's unit of work.
/// </summary>
public interface ISalesTeamMembershipCloser
{
    Task<int> CloseAllForUserAsync(Guid companyId, Guid userId, CancellationToken cancellationToken = default);
}
