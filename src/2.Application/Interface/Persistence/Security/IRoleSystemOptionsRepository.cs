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
    /// Retrieves a paged permission list with optional filters.
    /// </summary>
    Task<(IReadOnlyList<RoleSystemOptionReadModel> Items, int TotalCount)> GetPagedWithNamesAsync(
        RoleSystemOptionQueryFilter filter,
        bool ignoreCompanyFilter = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Filter parameters for paged RoleSystemOption queries.
/// </summary>
public sealed record RoleSystemOptionQueryFilter(
    Guid? CompanyId,
    Guid? RoleId,
    Guid? SystemOptionId,
    string? RoleName,
    string? SystemOptionName,
    string? CompanyName,
    bool? CanRead,
    bool? CanCreate,
    bool? CanUpdate,
    bool? CanDelete,
    int Offset,
    int PageSize);

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
