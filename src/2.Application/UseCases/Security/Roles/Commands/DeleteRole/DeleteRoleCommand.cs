using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Security.Roles.Commands.DeleteRole;

/// <summary>
/// Command to soft-delete an ApplicationRole. System-default roles are rejected by the handler.
/// </summary>
public sealed record DeleteRoleCommand(Guid Id) : IRequest<Response<bool>>;
