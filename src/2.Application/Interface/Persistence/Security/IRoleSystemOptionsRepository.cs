using JOIN.Application.DTO.Security;
using JOIN.Domain.Security;

namespace JOIN.Application.Interface.Persistence.Security;

/// <summary>
/// Contract for RoleSystemOption persistence operations with optimized read models.
/// </summary>
public interface IRoleSystemOptionsRepository : IGenericRepository<RoleSystemOption>
{
    /// <summary>
    /// Loads an active <see cref="RoleSystemOption"/> by identifier and company, ignoring the global tenant query filter.
    /// Used by commands when the caller supplies an explicit company scope (for example, administrative flows).
    /// </summary>
    Task<RoleSystemOption?> GetTrackedActiveByIdAndCompanyAsync(Guid id, Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether an active permission rule already exists for the same company, role, and option.
    /// </summary>
    Task<bool> ExistsByRoleAndOptionAsync(Guid companyId, Guid roleId, Guid systemOptionId);

    /// <summary>
    /// Retrieves a single permission rule with role and system option names.
    /// </summary>
    Task<RoleSystemOptionReadModel?> GetWithNamesAsync(Guid id, Guid? companyId = null);

    /// <summary>
    /// Loads the display names (Role, SystemOption, Company) for a permission rule
    /// using the same EF Core <c>DbContext</c> / connection as the write path.
    /// Safe to invoke inside the open <c>TransactionBehavior</c> transaction because it
    /// does not open a second connection — a fresh Dapper connection would otherwise
    /// block on the X-lock held by the outer transaction until the command timeout
    /// fires (see SPEC 22 / SPEC 23 readback notes).
    /// </summary>
    Task<RoleSystemOptionNames?> GetNamesByIdAndCompanyAsync(Guid id, Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active (GcRecord = 0) <see cref="RoleSystemOption"/> rows for the given
    /// role in the given tenant. Used by the role clone path (SPEC 24) to copy the
    /// origin role's permission catalog onto the newly created role.
    /// Reads from a separate Dapper connection — safe because this query targets the
    /// <em>origin</em> role's rows, which are not X-locked by the outer transaction that
    /// is staging the new role + new RoleSystemOption rows.
    /// </summary>
    Task<IReadOnlyList<RoleSystemOption>> GetActiveByRoleAndCompanyAsync(
        Guid roleId,
        Guid companyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full permissions matrix for the given role in the given tenant.
    /// Result includes every active <c>SystemOption</c> (even those without a
    /// <c>RoleSystemOption</c> row) so the UI can paint the entire grid.
    /// Returns <c>null</c> when the role does not exist or is soft-deleted.
    /// </summary>
    Task<RoleSystemOptionMatrixDto?> GetMatrixByRoleAsync(
        Guid roleId,
        Guid companyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a "replace set" diff between the current <c>RoleSystemOption</c> rows
    /// for <c>(roleId, companyId)</c> and the supplied <paramref name="newItems"/> in a
    /// single SQL transaction (no EF change tracker). Inserts missing rows, updates
    /// existing rows' flags, soft-deletes rows absent from the set.
    /// Returns the IDs of the rows that ended up in each bucket.
    /// </summary>
    Task<(IReadOnlyList<Guid> Created, IReadOnlyList<Guid> Updated, IReadOnlyList<Guid> Removed)>
        BulkUpsertAsync(
            Guid roleId,
            Guid companyId,
            IReadOnlyList<RoleSystemOption> newItems,
            CancellationToken cancellationToken = default);
}

/// <summary>
/// Read model for RoleSystemOption with role and option names.
/// </summary>
public sealed record RoleSystemOptionReadModel
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid RoleId { get; init; }
    public string RoleName { get; init; } = string.Empty;
    public Guid SystemOptionId { get; init; }
    public string SystemOptionName { get; init; } = string.Empty;
    public string CompanyName { get; init; } = string.Empty;
    public bool CanRead { get; init; }
    public bool CanCreate { get; init; }
    public bool CanUpdate { get; init; }
    public bool CanDelete { get; init; }
    public DateTime Created { get; init; }
}

/// <summary>
/// Projection of the three display names attached to a permission rule.
/// Returned by the EF-only readback so commands can populate
/// <c>RoleName</c>, <c>SystemOptionName</c> and <c>CompanyName</c> on the DTO
/// without opening a second SQL connection (see SPEC 23).
/// </summary>
public sealed record RoleSystemOptionNames(string RoleName, string SystemOptionName, string CompanyName);
