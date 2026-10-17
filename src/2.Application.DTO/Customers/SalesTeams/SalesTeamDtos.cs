using JOIN.Application.DTO.Common;
using JOIN.Application.DTO.Customers.LeadSettings;

namespace JOIN.Application.DTO.Customers.SalesTeams;

/// <summary>A management role (SPEC 60). <c>LeadScope</c>: 1 Own, 2 Team, 3 Company.</summary>
public sealed record TeamRoleDto : CommercialCatalogItemDto
{
    public int LeadScope { get; init; }
    public bool IsLeader { get; init; }
    public bool CanReassign { get; init; }
}

/// <summary>A sales team with its current members count and leader (SPEC 60).</summary>
public record SalesTeamDto : SoftDeletableDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public int CurrentMembersCount { get; init; }
    public Guid? LeaderUserId { get; init; }
    public string? LeaderName { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>A sales team with every membership, current and historical (<c>GET /SalesTeams/{id}</c>).</summary>
public sealed record SalesTeamDetailDto : SalesTeamDto
{
    public IReadOnlyList<SalesTeamMemberDto> Members { get; init; } = [];
}

/// <summary>A membership of a user in a sales team (SPEC 60).</summary>
public sealed record SalesTeamMemberDto
{
    public Guid Id { get; init; }
    public Guid SalesTeamId { get; init; }
    public string SalesTeamName { get; init; } = string.Empty;
    public bool SalesTeamIsActive { get; init; }
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public Guid TeamRoleId { get; init; }
    public string TeamRoleCode { get; init; } = string.Empty;
    public string TeamRoleName { get; init; } = string.Empty;
    /// <summary><c>LeadScope</c> of the role: 1 Own, 2 Team, 3 Company.</summary>
    public int LeadScope { get; init; }
    public bool IsLeader { get; init; }
    public bool CanReassign { get; init; }
    public bool IsPrimary { get; init; }
    public DateOnly ValidFrom { get; init; }
    public DateOnly? ValidTo { get; init; }
    /// <summary>Started and not ended today.</summary>
    public bool IsCurrent { get; init; }
}
