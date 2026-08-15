using JOIN.Application.DTO.Security;
using JOIN.Domain.Security;

namespace JOIN.Application.Interface.Persistence.Security;

/// <summary>
/// Persistence contract for ApplicationRole.
/// Writes use EF Core (tracked, soft-delete) for unit-of-work compatibility;
/// reads use Dapper for performance.
/// </summary>
public interface IRoleRepository
{
    /// <summary>
    /// Returns the active (GcRecord = 0) role matching the given id, projected to <see cref="RoleDto"/>, or null.
    /// <paramref name="companyId"/> is required because <see cref="RoleDto.PermissionsCount"/> is tenant-scoped
    /// (counts active RoleSystemOption rows for that CompanyId only).
    /// </summary>
    Task<RoleDto?> GetByIdAsync(Guid id, Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a page of active roles filtered by optional name substring and active flag, plus the total count.
    /// <paramref name="companyId"/> is required because each row's <see cref="RoleDto.PermissionsCount"/> is tenant-scoped.
    /// </summary>
    Task<(IReadOnlyList<RoleDto> Items, int Total)> GetPagedAsync(
        string? nameFilter,
        bool? isActive,
        int page,
        int pageSize,
        Guid companyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a new role through the EF Core change tracker (audit interceptor writes Created/CreatedBy).
    /// </summary>
    Task AddAsync(ApplicationRole role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracks the given role for update. Caller must invoke <see cref="JOIN.Application.Interface.Persistence.IUnitOfWork.SaveAsync"/>
    /// (or DbContext.SaveChangesAsync) for the change to be persisted.
    /// </summary>
    Task UpdateAsync(ApplicationRole role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when an active role with the given normalized name exists.
    /// </summary>
    Task<bool> ExistsByNameAsync(string normalizedName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when an active role with the given normalized name exists excluding the supplied id (used on update rename collision).
    /// </summary>
    Task<bool> ExistsByNameExceptIdAsync(string normalizedName, Guid excludedId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the tracked role entity for update paths (Update/Delete commands).
    /// </summary>
    Task<ApplicationRole?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when the role exists and is active (GcRecord == 0).
    /// Used by downstream validators (e.g. RoleCompany junction creation) that must reject
    /// references to missing or soft-deleted roles.
    /// </summary>
    Task<bool> ExistsAndActiveAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts active (GcRecord = 0) <see cref="Domain.Security.UserRoleCompany"/> rows
    /// referencing the given role for the given tenant. Tenant-scoped to avoid cross-company
    /// leaks: a role with users in another <c>CompanyId</c> returns <c>0</c>.
    /// Used by the delete path to enforce <c>ROLE_HAS_USERS</c>.
    /// </summary>
    Task<int> CountActiveUsersByRoleIdAsync(
        Guid roleId,
        Guid companyId,
        CancellationToken cancellationToken = default);
}
