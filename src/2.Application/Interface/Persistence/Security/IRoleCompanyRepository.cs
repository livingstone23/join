// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.DTO.Security.RoleCompany;
using JOIN.Domain.Security;

namespace JOIN.Application.Interface.Persistence.Security;

/// <summary>
/// Persistence contract for the RoleCompany junction.
/// Reads are Dapper-backed (raw SQL with JOIN to Security.Roles);
/// writes are EF Core-backed (tracked entities + IUnitOfWork.SaveChangesAsync).
/// Soft delete follows the project convention: the command handler calls
/// <see cref="BaseAuditableEntity.MarkAsDeleted"/> on the loaded entity and
/// persists via <see cref="UpdateAsync"/> + SaveChanges.
/// </summary>
public interface IRoleCompanyRepository
{
    /// <summary>
    /// Loads the detailed projection (with RoleName/IsSystemDefault from Security.Roles) for a single link,
    /// scoped to <paramref name="tenantId"/>. Returns null when not found or cross-tenant.
    /// </summary>
    Task<RoleCompanyDto?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a paged list of lightweight projections for the supplied tenant.
    /// Optional filters narrow by RoleId and active flag.
    /// </summary>
    Task<(IReadOnlyList<RoleCompanyListItemDto> Items, int Total)> GetPagedAsync(
        Guid tenantId,
        Guid? roleIdFilter,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new RoleCompany entity to the change tracker (caller persists via IUnitOfWork).
    /// </summary>
    Task AddAsync(RoleCompany entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists tracked changes for an existing RoleCompany entity (caller persists via IUnitOfWork).
    /// </summary>
    Task UpdateAsync(RoleCompany entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a tracked entity (skipping the global tenant query filter) for mutation paths.
    /// Filters by Id, CompanyId == <paramref name="tenantId"/>, and GcRecord == 0.
    /// Returns null when not found, soft-deleted, or cross-tenant.
    /// </summary>
    Task<RoleCompany?> GetByIdForUpdateAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether an active (GcRecord == 0) link already exists for the same (RoleId, CompanyId) pair.
    /// Used to translate unique-index violations into 409 ROLE_COMPANY_DUPLICATE.
    /// </summary>
    Task<bool> ExistsActiveLinkAsync(Guid roleId, Guid companyId, CancellationToken cancellationToken = default);
}
