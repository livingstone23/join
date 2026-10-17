using JOIN.Domain.Audit;

namespace JOIN.Domain.Customers.Leads;

/// <summary>
/// Expected relationship with the person of a lead (table <c>Customer.RelationshipTypes</c>, SPEC 59): possible
/// customer, supplier, partner… <see cref="OnWin"/> says whether winning creates or confirms the customer.
/// </summary>
public class RelationshipType : BaseTenantEntity, ICommercialCatalogItem
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public RelationshipOnWin OnWin { get; set; } = RelationshipOnWin.None;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
