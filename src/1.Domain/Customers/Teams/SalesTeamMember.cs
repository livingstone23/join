using JOIN.Domain.Audit;

namespace JOIN.Domain.Customers.Teams;

/// <summary>
/// Membership of a user in a sales team (table <c>Customer.SalesTeamMembers</c>, SPEC 60). A leave never deletes the
/// row: it closes it with <see cref="ValidTo"/>. A user has at most one open membership per team and one open primary
/// membership.
/// </summary>
public class SalesTeamMember : BaseTenantEntity
{
    public Guid SalesTeamId { get; set; }
    public Guid UserId { get; set; }
    public Guid TeamRoleId { get; set; }
    /// <summary>The team proposed when the user creates a lead.</summary>
    public bool IsPrimary { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    public virtual SalesTeam SalesTeam { get; set; } = null!;
    public virtual TeamRole TeamRole { get; set; } = null!;

    /// <summary>Not closed yet (the coordinators work on open memberships).</summary>
    public bool IsOpen => ValidTo is null;

    /// <summary>Current on <paramref name="day"/>: started and not ended.</summary>
    public bool IsCurrentOn(DateOnly day) => ValidFrom <= day && (ValidTo is null || ValidTo >= day);
}
