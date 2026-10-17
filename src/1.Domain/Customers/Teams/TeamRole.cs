using JOIN.Domain.Audit;
using JOIN.Domain.Customers.Leads;

namespace JOIN.Domain.Customers.Teams;

/// <summary>
/// Management role of a sales team member (table <c>Customer.TeamRoles</c>, SPEC 60): leader, coordinator, commercial
/// supervisor, member. Not a security role: it only sets the lead scope inside the customers module.
/// </summary>
public class TeamRole : BaseTenantEntity, ICommercialCatalogItem
{
    public static class Codes
    {
        public const string Supervisor = "SUPERVISOR";
        public const string Leader = "LEADER";
        public const string Coordinator = "COORDINATOR";
        public const string Member = "MEMBER";
    }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public LeadScope LeadScope { get; set; } = LeadScope.Own;
    /// <summary>One current leader per team holds a role with this flag.</summary>
    public bool IsLeader { get; set; }
    /// <summary>Can reassign the leads of the team (or of the company, with <see cref="LeadScope.Company"/>).</summary>
    public bool CanReassign { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
