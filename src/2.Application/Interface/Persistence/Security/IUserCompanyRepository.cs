// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Security;

namespace JOIN.Application.Interface.Persistence.Security;

/// <summary>
/// Persistence contract for the UserCompany membership table.
/// SPEC 38: UserCompany is the only BaseTenantEntity WITHOUT a global CompanyId query filter
/// (login has no CompanyId claim yet, and we must read across the user's companies to pick a
/// default). Every read here filters by UserId (and CompanyId when applicable) at the query,
/// never in memory — replacing the previous "load the whole table" pattern.
/// </summary>
public interface IUserCompanyRepository
{
    /// <summary>
    /// Returns the active (GcRecord == 0) memberships for a single user. Used during login,
    /// refresh, and SwitchCompany to pick the user's default tenant without loading other users.
    /// </summary>
    Task<IReadOnlyList<UserCompany>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when <paramref name="userId"/> has an active membership in
    /// <paramref name="companyId"/>. Used by CreateTicket/UpdateTicket to validate that the
    /// assigned user actually belongs to the company before creating the ticket.
    /// </summary>
    Task<bool> IsActiveMemberAsync(Guid userId, Guid companyId, CancellationToken cancellationToken = default);
}