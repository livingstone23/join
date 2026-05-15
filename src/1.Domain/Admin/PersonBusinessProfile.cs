using JOIN.Domain.Audit;



namespace JOIN.Domain.Admin;



/// <summary>
/// Holds specific commercial and business information for Legal persons 
/// or independent professionals (B2B profiling).
/// </summary>
public class PersonBusinessProfile : BaseTenantEntity
{
    
    /// <summary>
    /// Foreign key to the core Person entity.
    /// </summary>
    /// <value></value>
    /// <summary>
    /// Foreign key to the core Person entity.
    /// </summary>
    public Guid PersonId { get; set; }

    /// <summary>
    /// Foreign key to a Catalog of Industries or Economic Sectors (e.g., "Technology", "Finance", "Retail").
    /// </summary>
    public Guid IndustryId { get; set; }

    /// <summary>
    /// Foreign key to a Catalog of Tax Regimes (depending on the country, e.g., "Régimen General", "Régimen Simplificado").
    /// </summary>
    public Guid TaxRegimeId { get; set; }

    /// <summary>
    /// The official corporate website of the business.
    /// </summary>
    public string? Website { get; set; }

    /// <summary>
    /// The date the company was legally founded or registered.
    /// </summary>
    public DateTime? FoundationDate { get; set; }

    public bool IsActive { get; private set; } = true;

    // --- Navigation Properties ---
    public virtual Person Person { get; set; } = null!;
    public virtual Industry Industry { get; set; } = null!;
    public virtual TaxRegime TaxRegime { get; set; } = null!;

    // --- Domain Behavior ---

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
    }

    public void Reactivate()
    {
        if (IsActive) return;
        IsActive = true;
    }


}