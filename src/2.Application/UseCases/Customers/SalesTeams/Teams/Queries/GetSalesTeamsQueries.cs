using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.SalesTeams;
using MediatR;

namespace JOIN.Application.UseCases.Customers.SalesTeams.Teams.Queries;

/// <summary><c>GET /SalesTeams</c> (SPEC 60): the company's teams with current members count and leader, not paged.</summary>
public sealed record GetSalesTeamsQuery(bool? IncludeInactive = null, bool? IncludeDeleted = null, Guid? CompanyId = null)
    : IRequest<Response<IReadOnlyList<SalesTeamDto>>>;

/// <summary><c>GET /SalesTeams/{id}</c>: the team with every membership, current and historical.</summary>
public sealed record GetSalesTeamByIdQuery(Guid Id, bool? IncludeDeleted = null, Guid? CompanyId = null) : IRequest<Response<SalesTeamDetailDto>>;

/// <summary><c>GET /SalesTeams/{id}/members</c>: memberships of the team; <c>currentOnly</c> leaves out the closed ones.</summary>
public sealed record GetSalesTeamMembersQuery(Guid SalesTeamId, bool? CurrentOnly = null, Guid? CompanyId = null)
    : IRequest<Response<IReadOnlyList<SalesTeamMemberDto>>>;

/// <summary>
/// <c>GET /SalesTeams/me</c> (SPEC 60 C.12): the current memberships of the token's user in the token's company.
/// </summary>
public sealed record GetMySalesTeamsQuery : IRequest<Response<IReadOnlyList<SalesTeamMemberDto>>>;
