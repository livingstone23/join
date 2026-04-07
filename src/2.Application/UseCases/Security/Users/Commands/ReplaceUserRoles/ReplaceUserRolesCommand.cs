using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Users.Commands.ReplaceUserRoles;

/// <summary>
/// Represents the command used to replace the full set of roles assigned to a specific user.
/// The supplied role collection is interpreted as the definitive final state that must remain associated with the target account once the command completes.
/// </summary>
/// <param name="UserId">The unique identifier of the user whose role assignments should be replaced.</param>
/// <param name="Roles">The final list of roles that must remain assigned to the target user after processing.</param>
public sealed record ReplaceUserRolesCommand(Guid UserId, IReadOnlyCollection<string> Roles)
    : IRequest<Response<UserWithRolesDto>>;
