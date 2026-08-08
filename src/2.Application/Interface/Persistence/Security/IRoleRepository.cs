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
    /// </summary>
    Task<RoleDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a page of active roles filtered by optional name substring and active flag, plus the total count.
    /// </summary>
    Task<(IReadOnlyList<RoleDto> Items, int Total)> GetPagedAsync(
        string? nameFilter,
        bool? isActive,
        int page,
        int pageSize,
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
    /// Marks the role as soft-deleted by incrementing GcRecord and stamping LastModified/LastModifiedBy in a single UPDATE.
    /// Returns the number of rows affected (0 if the role is missing or already soft-deleted).
    /// </summary>
    Task<int> SoftDeleteAsync(Guid id, string modifiedBy, CancellationToken cancellationToken = default);

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
}
