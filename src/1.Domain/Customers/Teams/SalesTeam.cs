using JOIN.Domain.Audit;

namespace JOIN.Domain.Customers.Teams;

/// <summary>Sales team of the customers module (table <c>Customer.SalesTeams</c>, SPEC 60).</summary>
public class SalesTeam : BaseTenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>An inactive team is not chosen for new leads.</summary>
    public bool IsActive { get; set; } = true;
}
