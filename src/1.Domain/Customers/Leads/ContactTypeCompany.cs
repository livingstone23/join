using JOIN.Domain.Audit;

namespace JOIN.Domain.Customers.Leads;

/// <summary>A company's copy of a master contact type (table <c>Customer.ContactTypeCompanies</c>, SPEC 59).</summary>
public class ContactTypeCompany : BaseTenantEntity
{
    public Guid ContactTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public virtual ContactType ContactType { get; set; } = null!;
}
