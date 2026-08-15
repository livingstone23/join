using System.Text.Json.Serialization;
using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

/// <summary>
/// Command used to soft delete a role-system-option permission rule.
/// </summary>
public sealed record DeleteRoleSystemOptionCommand(
    [property: JsonIgnore]
    Guid Id,
    Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
