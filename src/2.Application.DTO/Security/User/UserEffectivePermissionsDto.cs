namespace JOIN.Application.DTO.Security;

/// <summary>
/// Effective permissions of a user inside a single tenant. Reuses the matrix DTOs
/// from SPEC 25 so the panel renders the same grid it already paints for roles.
/// <c>Granted</c> on each option is the logical OR of the flags of every active
/// role the user holds in the tenant. <c>Supports</c> still comes from
/// <c>SystemOptions</c>. When the user has no role in the tenant, <c>RoleIds</c>
/// and <c>RoleNames</c> are empty and every <c>Granted</c> flag is <c>false</c>
/// — same contract as <c>GET /RoleSystemOptions/matrix</c>.
/// </summary>
public sealed record UserEffectivePermissionsDto(
    Guid UserId,
    Guid CompanyId,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<string> RoleNames,
    IReadOnlyList<RoleSystemOptionMatrixModuleDto> Modules);