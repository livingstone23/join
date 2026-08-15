namespace JOIN.Application.DTO.Security;

/// <summary>
/// Data transfer object for the ApplicationRole catalog.
/// Used by detailed GET, create, and update responses.
/// </summary>
/// <param name="PermissionsCount">
/// Number of active <c>RoleSystemOption</c> rows this role currently has in the caller's tenant.
/// Always tenant-scoped: a role with permissions in another <c>CompanyId</c> returns <c>0</c> here.
/// Projected via correlated subquery on <c>GetByIdAsync</c> / <c>GetPagedAsync</c> — never materialized in C#.
/// </param>
public sealed record RoleDto(
    Guid Id,
    string Name,
    string NormalizedName,
    string? Description,
    bool IsSystemDefault,
    string? CreatedBy,
    DateTime Created,
    int PermissionsCount = 0);
