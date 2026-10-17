using JOIN.Domain.Audit;

namespace JOIN.Domain.Customers.Leads;

/// <summary>Reason for winning, losing or inactivating a lead (table <c>Customer.LeadReasons</c>, SPEC 59).</summary>
public class LeadReason : BaseTenantEntity, ICommercialCatalogItem
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public LeadReasonUsage UsedFor { get; set; } = LeadReasonUsage.Lost;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
