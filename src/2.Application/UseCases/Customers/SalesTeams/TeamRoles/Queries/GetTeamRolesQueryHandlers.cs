using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.SalesTeams;
using JOIN.Application.Interface;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;
using MediatR;

namespace JOIN.Application.UseCases.Customers.SalesTeams.TeamRoles.Queries;

internal static class TeamRoleQuerySql
{
    internal const string Table = "teamroles";
    internal const string Columns = ", t.LeadScope, t.IsLeader, t.CanReassign";
}

/// <summary>Handles <see cref="GetTeamRolesQuery"/>.</summary>
public sealed class GetTeamRolesQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetTeamRolesQuery, Response<IReadOnlyList<TeamRoleDto>>>
{
    public Task<Response<IReadOnlyList<TeamRoleDto>>> Handle(GetTeamRolesQuery request, CancellationToken cancellationToken) =>
        CommercialCatalogQuery.ListAsync<TeamRoleDto>(
            connectionFactory, currentUserService, TeamRoleQuerySql.Table, TeamRoleQuerySql.Columns,
            request.IncludeInactive, request.IncludeDeleted, request.CompanyId, "Team roles", cancellationToken);
}

/// <summary>Handles <see cref="GetTeamRoleByIdQuery"/>.</summary>
public sealed class GetTeamRoleByIdQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetTeamRoleByIdQuery, Response<TeamRoleDto>>
{
    public Task<Response<TeamRoleDto>> Handle(GetTeamRoleByIdQuery request, CancellationToken cancellationToken) =>
        CommercialCatalogQuery.GetByIdAsync<TeamRoleDto>(
            connectionFactory, currentUserService, TeamRoleQuerySql.Table, TeamRoleQuerySql.Columns,
            request.Id, request.IncludeDeleted, request.CompanyId, SalesTeamErrorCodes.TeamRoleNotFound, "team role", cancellationToken);
}
