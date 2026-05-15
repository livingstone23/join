using JOIN.Domain.Audit;



namespace JOIN.Domain.Admin;



/// <summary>
/// Represents the economic capacity and declared income of a Person.
/// Essential for product offerings, credit scoring, or KYC (Know Your Customer) compliance.
/// </summary>
public class PersonFinancialProfile : BaseTenantEntity
{

    
    /// <summary>
    /// Foreign key to the core Person entity.
    /// </summary>
    /// <value></value>
    public Guid PersonId { get; set; }

    /// <summary>
    /// Foreign key to a Catalog of Income Ranges (e.g., "$0 - $1,000", "$1,001 - $5,000").
    /// Using a catalog allows administrators to dynamically adjust brackets without code changes.
    /// </summary>
    public Guid IncomeRangeId { get; set; }

    /// <summary>
    /// Identifies the source of the income (e.g., "Salary", "Investments", "Business Owner").
    /// This could also point to a Catalog if you need strict standardization.
    /// </summary>
    public string SourceOfFunds { get; set; } = string.Empty;

    /// <summary>
    /// The exact date when the customer declared this financial information.
    /// Critical for compliance, to trigger reminders to update financial data annually.
    /// </summary>
    public DateTime DeclaredDate { get; set; }

    /// <summary>
    /// Indicates if this is the most recent and valid financial profile.
    /// </summary>
    public bool IsCurrent { get; private set; } = true;

    public bool IsActive { get; private set; } = true;

    // --- Navigation Properties ---
    public virtual Person Person { get; set; } = null!;
    public virtual IncomeRange IncomeRange { get; set; } = null!;

    // --- Domain Behavior ---

    /// <summary>
    /// Archives this financial profile, usually called when a new one is declared.
    /// </summary>
    public void Archive()
    {
        if (!IsCurrent) return;
        IsCurrent = false;
    }

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        IsCurrent = false; 
    }

    public void Reactivate()
    {
        if (IsActive) return;
        IsActive = true;
    }
    
}