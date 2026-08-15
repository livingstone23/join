namespace JOIN.Application.DTO.Security;

/// <summary>
/// Seven <c>Can*</c> flags as defined by SPEC 22 / <c>PermissionFlags</c>.
/// Used twice in the matrix: <c>Supports</c> describes the defaults from
/// <c>SystemOptions</c> (what the option can theoretically do),
/// <c>Granted</c> describes the actual <c>RoleSystemOption</c> row for the role
/// (what the role can do on that option). All seven flags return <c>false</c>
/// when no <c>RoleSystemOption</c> row exists for the (role, option) pair
/// (LEFT JOIN collapsed to <c>false</c>).
/// </summary>
public sealed record RoleSystemOptionSupportFlags(
    bool CanRead,
    bool CanCreate,
    bool CanUpdate,
    bool CanDelete,
    bool CanDownload,
    bool CanExport,
    bool CanExecute);

public sealed record RoleSystemOptionGrantedFlags(
    bool CanRead,
    bool CanCreate,
    bool CanUpdate,
    bool CanDelete,
    bool CanDownload,
    bool CanExport,
    bool CanExecute);

/// <summary>
/// Single cell of the permissions matrix. <c>Supports</c> comes from
/// <c>SystemOptions</c>; <c>Granted</c> comes from <c>RoleSystemOptions</c>
/// for the requested role.
/// </summary>
public sealed record RoleSystemOptionMatrixOptionDto(
    Guid SystemOptionId,
    string Name,
    string Route,
    RoleSystemOptionSupportFlags Supports,
    RoleSystemOptionGrantedFlags Granted);

/// <summary>
/// One module worth of options. The UI renders an accordion per module
/// (server-side grouping saves the client from cross-referencing two paginated endpoints).
/// </summary>
public sealed record RoleSystemOptionMatrixModuleDto(
    Guid ModuleId,
    string ModuleName,
    IReadOnlyList<RoleSystemOptionMatrixOptionDto> Options);

/// <summary>
/// Full permissions matrix for a role in the caller's tenant.
/// Even when the role has zero granted rows, <c>Modules</c> still contains
/// every active <c>SystemOption</c> with <c>Granted = all-false</c> so the UI
/// can paint the full grid.
/// </summary>
public sealed record RoleSystemOptionMatrixDto(
    Guid RoleId,
    string RoleName,
    IReadOnlyList<RoleSystemOptionMatrixModuleDto> Modules);