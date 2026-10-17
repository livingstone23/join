using JOIN.Domain.Audit;

namespace JOIN.Domain.Customers.Leads;

/// <summary>
/// Lead module rules of a company (table <c>Customer.LeadCompanySettings</c>, one row per company, SPEC 59). A company
/// without a row behaves with <see cref="Defaults"/>.
/// </summary>
public class LeadCompanySettings : BaseTenantEntity
{
    public const string DefaultCodePrefix = "LD-";
    public const int DefaultCodeSequenceLength = 6;

    public DuplicateOpenLeadPolicy DuplicateOpenLeadPolicy { get; set; } = DuplicateOpenLeadPolicy.Warn;
    /// <summary>SPEC 58 rule 4: a person needs an identification to become a customer.</summary>
    public bool RequireIdentificationForCustomer { get; set; } = true;
    public string CodePrefix { get; set; } = DefaultCodePrefix;
    public int CodeSequenceLength { get; set; } = DefaultCodeSequenceLength;

    /// <summary>The values a company without a row uses.</summary>
    public static LeadCompanySettings Defaults(Guid companyId) => new() { CompanyId = companyId };
}
