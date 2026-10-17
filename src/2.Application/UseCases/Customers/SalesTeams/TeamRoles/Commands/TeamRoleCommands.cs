using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.SalesTeams;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;

namespace JOIN.Application.UseCases.Customers.SalesTeams.TeamRoles.Commands;

/// <summary>Fields of a management role beyond the shared catalog ones.</summary>
public interface ITeamRoleInput : ICommercialCatalogInput
{
    /// <summary><c>LeadScope</c>: 1 Own, 2 Team, 3 Company.</summary>
    int LeadScope { get; }
    bool IsLeader { get; }
    bool CanReassign { get; }
}

/// <summary><c>POST /TeamRoles</c> (SPEC 60).</summary>
public sealed record CreateTeamRoleCommand : ITeamRoleInput, ITransactionalCommand<Response<TeamRoleDto>>
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    public int LeadScope { get; init; } = 1;
    public bool IsLeader { get; init; }
    public bool CanReassign { get; init; }
}

/// <summary><c>PUT /TeamRoles/{id}</c> (SPEC 60): changing the scope changes what every holder sees on the next call.</summary>
public sealed record UpdateTeamRoleCommand : ITeamRoleInput, ICommercialCatalogUpdate, ITransactionalCommand<Response<TeamRoleDto>>
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    public int LeadScope { get; init; } = 1;
    public bool IsLeader { get; init; }
    public bool CanReassign { get; init; }
}

/// <summary><c>DELETE /TeamRoles/{id}</c>: not while a current membership holds it (<c>TEAM_ROLE_IN_USE</c>).</summary>
public sealed record DeleteTeamRoleCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;

/// <summary><c>POST /TeamRoles/{id}/restore</c> (SPEC 41-post, SuperAdmin).</summary>
public sealed record RestoreTeamRoleCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
