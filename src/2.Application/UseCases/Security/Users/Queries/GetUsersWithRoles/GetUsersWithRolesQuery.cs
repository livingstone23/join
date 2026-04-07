using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Users.Queries.GetUsersWithRoles;

/// <summary>
/// Represents the query used to retrieve the active user list together with the roles currently assigned to each account.
/// This request supports administrative views that require a consolidated projection of user identity and role membership.
/// </summary>
public sealed record GetUsersWithRolesQuery : IRequest<Response<IEnumerable<UserWithRolesDto>>>;
