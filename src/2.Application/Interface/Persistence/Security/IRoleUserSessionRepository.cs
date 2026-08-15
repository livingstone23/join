using JOIN.Application.DTO.Security;

namespace JOIN.Application.Interface.Persistence.Security;

/// <summary>
/// Identifies the underlying source table for a session lookup result.
/// </summary>
public enum SessionType
{
    UserRefreshToken = 0,
    UserConnectionLog = 1,
}

/// <summary>
/// Compound lookup row returned by <see cref="IRoleUserSessionRepository.FindActiveByIdAsync"/>.
/// </summary>
/// <param name="Id">The row identifier in the source table.</param>
/// <param name="Type">Which table the row lives in.</param>
public sealed record SessionLookupResult(Guid Id, SessionType Type);

/// <summary>
/// Backs AccountController session-revoke flows (<c>DELETE /account/sessions/{id}</c> and
/// <c>POST /account/sessions/revoke-others</c>). Reads cross <c>UserConnectionLogs</c> and
/// <c>UserRefreshTokens</c> via UNION ALL; soft-deletes via UPDATE on the right table.
/// </summary>
public interface IRoleUserSessionRepository
{
    /// <summary>
    /// Locates a non-revoked session row by Id from either source table.
    /// Returns <c>null</c> when the Id matches no live row.
    /// </summary>
    Task<SessionLookupResult?> FindActiveByIdAsync(Guid sessionId, CancellationToken ct);

    /// <summary>
    /// Returns the owning user id for the supplied session Id. Cross-user revoke attempts
    /// (where the calling user is not the owner) compare against this value to fail with 404.
    /// </summary>
    Task<Guid?> GetUserIdBySessionIdAsync(Guid sessionId, CancellationToken ct);

    /// <summary>
    /// Soft-revokes every active <c>Security.UserRefreshTokens</c> row for the user except
    /// the current refresh token (when supplied). Sets <c>IsRevoked = 1</c> and updates
    /// <c>LastModified</c>; tokens already revoked stay untouched.
    /// </summary>
    Task<int> SoftRevokeRefreshTokensExceptAsync(Guid userId, Guid? currentRefreshTokenId, DateTime utcNow, CancellationToken ct);

    /// <summary>
    /// Closes every active <c>Security.UserConnectionLogs</c> row for the user
    /// (<c>IsActiveSession = 0</c>, <c>DisconnectionDate = @utcNow</c>, <c>LastModified = @utcNow</c>).
    /// </summary>
    Task<int> SoftRevokeActiveConnectionsAsync(Guid userId, DateTime utcNow, CancellationToken ct);

    /// <summary>
    /// Soft-revokes a single <c>Security.UserRefreshTokens</c> row. Excludes the current
    /// refresh token Id when supplied so handlers can't accidentally revoke the live session.
    /// Returns the affected row count (0 or 1).
    /// </summary>
    Task<int> SoftRevokeSingleRefreshTokenAsync(Guid refreshTokenId, Guid? excludeCurrentRefreshTokenId, DateTime utcNow, CancellationToken ct);

    /// <summary>
    /// Closes a single <c>Security.UserConnectionLogs</c> row
    /// (<c>IsActiveSession = 0</c>, <c>DisconnectionDate = @utcNow</c>, <c>LastModified = @utcNow</c>).
    /// Returns the affected row count (0 or 1).
    /// </summary>
    Task<int> SoftRevokeSingleConnectionAsync(Guid connectionId, DateTime utcNow, CancellationToken ct);

    /// <summary>
    /// Returns the user's assigned role (id + name) for the supplied tenant. Empty when the
    /// user has no role assignment in the tenant.
    /// </summary>
    Task<IReadOnlyList<UserRoleLookupRow>> ListUserRolesInTenantAsync(Guid userId, Guid companyId, CancellationToken ct);

    /// <summary>
    /// Returns one row per active <c>Security.SystemOptions</c> in the tenant, with the
    /// OR of the <c>Security.RoleSystemOptions</c> flags across the supplied role ids
    /// (per-option). Empty when the tenant has no active options.
    /// </summary>
    Task<IReadOnlyList<PermissionFlagGridRow>> GetPermissionFlagGridAsync(
        IReadOnlyCollection<Guid> roleIds,
        Guid companyId,
        CancellationToken ct);
}

/// <summary>
/// One role assigned to a user inside a tenant. Returned by
/// <see cref="IRoleUserSessionRepository.ListUserRolesInTenantAsync"/>.
/// </summary>
/// <param name="RoleId">Role identifier.</param>
/// <param name="RoleName">Display name from <c>Security.Roles.Name</c>.</param>
public sealed record UserRoleLookupRow(Guid RoleId, string RoleName);

/// <summary>
/// Raw projection used by <see cref="IRoleUserSessionRepository.GetPermissionFlagGridAsync"/>.
/// One row per (module, option) inside the tenant, with the per-option supports from
/// <c>Security.SystemOptions</c> and the per-option granted = OR across supplied roles.
/// </summary>
public sealed record PermissionFlagGridRow(
    Guid ModuleId,
    string ModuleName,
    Guid SystemOptionId,
    string OptionName,
    string OptionRoute,
    int DisplayOrder,
    RoleSystemOptionSupportFlags Supports,
    RoleSystemOptionGrantedFlags Granted);
