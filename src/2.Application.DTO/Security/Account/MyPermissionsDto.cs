using JOIN.Application.DTO.Security;

namespace JOIN.Application.DTO.Security.Account;

/// <summary>
/// Response payload for <c>GET /api/v1/account/my-permissions</c> (SPEC 26 / F4).
/// Reports the matrix grid for the caller inside the tenant resolved from the JWT.
/// </summary>
public sealed record MyPermissionsDto
{
    /// <summary>
    /// Caller's user id (from the JWT <c>sub</c> claim).
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Tenant id used to read the matrix (JWT <c>CompanyId</c> claim or <c>X-Company-Id</c> header).
    /// </summary>
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Distinct role ids assigned to the user inside the tenant.
    /// Empty when the user has no roles.
    /// </summary>
    public IReadOnlyList<Guid> RoleIds { get; init; } = Array.Empty<Guid>();

    /// <summary>
    /// Combined permissions matrix. Always populated with every active
    /// <c>SystemOption</c> for the tenant; <c>Granted</c> is the OR of the
    /// user's effective flags per (module, option).
    /// <see cref="RoleSystemOptionMatrixDto.RoleName"/> carries the
    /// "&lt;first&gt; + N más" combined-name format when the user has multiple roles.
    /// </summary>
    public RoleSystemOptionMatrixDto Matrix { get; init; } = new(
        Guid.Empty,
        string.Empty,
        Array.Empty<RoleSystemOptionMatrixModuleDto>());
}
