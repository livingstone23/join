using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Roles.Commands.CreateRole;

/// <summary>
/// Command to create a new ApplicationRole.
/// </summary>
/// <param name="Name">Display name; will be trimmed and upper-cased into NormalizedName by the handler.</param>
/// <param name="Description">Optional free-text description (max 500 chars).</param>
/// <param name="IsSystemDefault">Indicates whether the role is a system-default role. Create will normally receive false.</param>
/// <param name="CloneFromRoleId">
/// Optional source role id. When supplied, the new role inherits the source's active
/// <c>RoleSystemOption</c> rows for the caller's tenant (same <c>SystemOptionId</c>,
/// same <c>Can*</c> flags, same <c>OrderMenu</c>, same <c>IsVisibleMenu</c>).
/// Does NOT clone <c>UserRoleCompany</c> nor <c>RoleCompany</c> — only the permission catalog.
/// </param>
public sealed record CreateRoleCommand(
    string Name,
    string? Description,
    bool IsSystemDefault,
    Guid? CloneFromRoleId = null)
    : IRequest<Response<RoleDto>>;
