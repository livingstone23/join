using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Roles.Commands.UpdateRole;

/// <summary>
/// Command to update an existing ApplicationRole. System-default roles only accept changes to Description,
/// so <paramref name="Name"/> is optional: when omitted, the existing role name is preserved.
/// </summary>
/// <param name="Id">Identifier of the role to update.</param>
/// <param name="Name">Display name (max 256 chars). Null leaves the existing name untouched.</param>
/// <param name="Description">Optional free-text description (max 500 chars).</param>
/// <param name="IsSystemDefault">New flag value. Promotion from false to true is rejected.</param>
public sealed record UpdateRoleCommand(Guid Id, string? Name, string? Description, bool IsSystemDefault)
    : IRequest<Response<RoleDto>>;
