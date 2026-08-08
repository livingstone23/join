using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Roles.Queries.GetRoleById;

/// <summary>
/// Query for fetching a single ApplicationRole by id.
/// </summary>
public sealed record GetRoleByIdQuery(Guid Id) : IRequest<Response<RoleDto>>;
