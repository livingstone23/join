using JOIN.Domain.Audit;

namespace JOIN.Domain.Customers.Leads;

/// <summary>Why a lead arrived (table <c>Customer.LeadSources</c>, SPEC 59).</summary>
public class LeadSource : BaseTenantEntity, ICommercialCatalogItem
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
