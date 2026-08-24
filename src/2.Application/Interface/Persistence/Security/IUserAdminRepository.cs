namespace JOIN.Application.Interface.Persistence.Security;

/// <summary>
/// Dapper-backed repository that exposes the cross-cutting reads/writes needed by the
/// admin user-lifecycle handlers introduced in SPEC 27 (items 15/16/17).
/// Reads and writes here bypass EF Core's change tracker and the global query filter on
/// <c>ApplicationUser</c>, which is exactly the point: the filter hides inactive users
/// from every EF query, and the admin endpoints must be able to see and mutate them.
/// All read queries filter <c>GcRecord = 0</c>; no method filters on <c>IsActive</c>.
/// </summary>
public interface IUserAdminRepository
{
    /// <summary>Soft-revokes every active refresh token of the user.</summary>
    Task<int> RevokeActiveRefreshTokensAsync(Guid userId, DateTime utcNow, CancellationToken ct = default);

    /// <summary>Closes every active connection log of the user.</summary>
    Task<int> CloseActiveConnectionsAsync(Guid userId, DateTime utcNow, CancellationToken ct = default);

    /// <summary>
    /// Returns a denormalized snapshot for admin lookups: user identity + active flag
    /// + membership existence in the supplied tenant. <c>null</c> when the user is
    /// soft-deleted or simply does not exist.
    /// </summary>
    Task<UserAdminSnapshot?> GetAdminSnapshotAsync(
        Guid userId, Guid companyId, CancellationToken ct = default);

    /// <summary>
    /// Flips <c>IsActive</c> on the user, recording the supplied reason and audit fields
    /// in one UPDATE. Returns <c>false</c> when no row was affected (user missing / soft-deleted).
    /// </summary>
    Task<bool> SetUserActiveStatusAsync(
        Guid userId,
        bool isActive,
        string reason,
        string? modifiedBy,
        DateTime utcNow,
        CancellationToken ct = default);

    /// <summary>True when the user has a non-soft-deleted <c>UserCompany</c> in the tenant.</summary>
    Task<bool> HasCompanyMembershipAsync(Guid userId, Guid companyId, CancellationToken ct = default);

    /// <summary>True when the user has any non-soft-deleted <c>UserCompany</c> in any tenant.</summary>
    Task<bool> HasAnyCompanyAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Returns the subset of <paramref name="roleIds"/> that exist and are linked to the
    /// supplied tenant via <c>RoleCompanies</c>. Cross-tenant role ids are filtered out.
    /// </summary>
    Task<IReadOnlyList<Guid>> FilterExistingRoleIdsAsync(
        IReadOnlyList<Guid> roleIds, Guid companyId, CancellationToken ct = default);

    /// <summary>Returns the company display name. <c>null</c> when not found or soft-deleted.</summary>
    Task<string?> GetCompanyNameAsync(Guid companyId, CancellationToken ct = default);
}

/// <summary>
/// Lightweight projection used by <see cref="IUserAdminRepository.GetAdminSnapshotAsync"/>.
/// Avoids loading the full <c>ApplicationUser</c> graph through EF.
/// </summary>
public sealed record UserAdminSnapshot(
    Guid UserId,
    string? Email,
    string? FirstName,
    bool IsActive,
    int GcRecord,
    bool HasMembership);
