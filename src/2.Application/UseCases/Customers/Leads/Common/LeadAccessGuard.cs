using Dapper;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Customers;
using JOIN.Domain.Customers.Leads;

namespace JOIN.Application.UseCases.Customers.Leads.Common;

/// <summary>
/// Applies the lead scope (SPEC 60, SPEC 61 B.12) once per request: SuperAdmin sees everything; others see the leads
/// their scope covers plus every inactive lead.
/// </summary>
public sealed class LeadAccessGuard(ILeadAccessScopeResolver resolver, ICurrentUserService currentUserService)
{
    public async Task<LeadAccess> ResolveAsync(Guid companyId, Guid userId, CancellationToken cancellationToken = default)
    {
        var isSuperAdmin = currentUserService.IsInRole("SuperAdmin");
        var scope = isSuperAdmin
            ? new LeadAccessScope(userId, true, [], [], true)
            : await resolver.ResolveAsync(companyId, userId, cancellationToken);
        return new LeadAccess(userId, isSuperAdmin, scope);
    }
}

/// <summary>The resolved access of the current user to the company's leads.</summary>
public sealed record LeadAccess(Guid UserId, bool IsSuperAdmin, LeadAccessScope Scope)
{
    /// <summary>Visible: covered by the scope, or inactive (rule 6).</summary>
    public bool CanSee(Lead lead) => CanSee(lead.Status, lead.OwnerUserId, lead.SalesTeamId);

    /// <summary>Visibility from the raw columns (Dapper reads).</summary>
    public bool CanSee(LeadStatus status, Guid ownerUserId, Guid? salesTeamId) =>
        status == LeadStatus.Inactive || IsSuperAdmin || Scope.Covers(ownerUserId, salesTeamId);

    /// <summary>The scope covers the lead (needed to work it: edit, inactivate…).</summary>
    public bool Covers(Lead lead) => IsSuperAdmin || Scope.Covers(lead.OwnerUserId, lead.SalesTeamId);

    /// <summary>Reassigning needs <c>CanReassign</c> on the lead's team or company-wide (B.12).</summary>
    public bool CanReassign(Lead lead) => IsSuperAdmin || Scope.CanReassign(lead.SalesTeamId);

    /// <summary>
    /// SQL condition with the same visibility for a Dapper query over <paramref name="alias"/> (empty when everything
    /// is visible). Adds <c>ScopeUserId</c> and <c>ScopeTeamIds</c> to <paramref name="parameters"/>.
    /// </summary>
    public string SqlFilter(string alias, DynamicParameters parameters)
    {
        if (Scope.IsCompanyWide)
        {
            return string.Empty;
        }

        parameters.Add("ScopeUserId", UserId);
        parameters.Add("ScopeTeamIds", Scope.TeamIds.ToArray());
        return $"AND ({alias}.Status = {(int)LeadStatus.Inactive} OR {alias}.OwnerUserId = @ScopeUserId OR {alias}.SalesTeamId = ANY(@ScopeTeamIds))";
    }
}
