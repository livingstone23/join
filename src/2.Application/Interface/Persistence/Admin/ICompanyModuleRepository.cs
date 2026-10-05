// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Admin;

namespace JOIN.Application.Interface.Persistence.Admin;

/// <summary>
/// Persistence contract for tenant-scoped company module assignments.
/// Writes are EF Core-backed and require explicit <c>tenantId</c> because the global
/// CompanyId query filter would otherwise lock reads/writes to the current user's token tenant.
/// SPEC 38: SuperAdmin cross-tenant writes (via TenantResolver) skip the global filter and apply
/// <c>CompanyId == tenantId &amp;&amp; GcRecord == 0</c> explicitly.
/// </summary>
public interface ICompanyModuleRepository
{
    /// <summary>
    /// Loads a tracked entity for mutation paths, skipping the global tenant filter
    /// and enforcing <c>Id == id &amp;&amp; CompanyId == tenantId &amp;&amp; GcRecord == 0</c>.
    /// Returns null when not found, soft-deleted, or cross-tenant.
    /// </summary>
    Task<CompanyModule?> GetByIdForUpdateAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether an active (GcRecord == 0) assignment already exists for the same
    /// (CompanyId, ModuleId) pair within the supplied tenant. Used to translate unique-index
    /// violations into 409 COMPANY_MODULE_ALREADY_EXISTS.
    /// </summary>
    Task<bool> ExistsActiveAssignmentAsync(Guid companyId, Guid moduleId, Guid tenantId, CancellationToken cancellationToken = default);
}