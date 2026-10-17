using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.SalesTeams;
using MediatR;

namespace JOIN.Application.UseCases.Customers.SalesTeams.TeamRoles.Queries;

/// <summary><c>GET /TeamRoles</c> (SPEC 59): every row of the company, not paged.</summary>
public sealed record GetTeamRolesQuery(
    bool? IncludeInactive = null,
    bool? IncludeDeleted = null,
    Guid? CompanyId = null) : IRequest<Response<IReadOnlyList<TeamRoleDto>>>;

/// <summary><c>GET /TeamRoles/{id}</c>.</summary>
public sealed record GetTeamRoleByIdQuery(Guid Id, bool? IncludeDeleted = null, Guid? CompanyId = null) : IRequest<Response<TeamRoleDto>>;
