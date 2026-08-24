using JOIN.Application.Common;
using JOIN.Application.DTO.Security.User;
using MediatR;

namespace JOIN.Application.UseCases.Security.Users.Commands.InviteUser;

/// <summary>
/// Invites a user to the caller's tenant. <see cref="ITransactionalCommand{TResponse}"/>
/// so that if the email send fails, the EF Core transaction rolls back and no orphan
/// row is left in <c>Security.Users</c> / <c>UserCompanies</c> / <c>UserRoleCompanies</c>.
/// </summary>
public sealed record InviteUserCommand(
    string Email,
    string FirstName,
    string LastName,
    IReadOnlyList<Guid> RoleIds)
    : ITransactionalCommand<Response<InviteUserResultDto>>;
