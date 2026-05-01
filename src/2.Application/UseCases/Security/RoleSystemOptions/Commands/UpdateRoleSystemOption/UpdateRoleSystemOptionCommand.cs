using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

/// <summary>
/// Command used to update the permission flags of an existing role-system-option rule.
/// </summary>
public sealed record UpdateRoleSystemOptionCommand(
    [property: JsonIgnore]
    Guid Id,
    Guid CompanyId,
    bool CanRead,
    bool CanCreate,
    bool CanUpdate,
    bool CanDelete)
    : IRequest<Response<RoleSystemOptionDto>>;
