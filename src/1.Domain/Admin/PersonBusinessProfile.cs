using JOIN.Domain.Audit;



namespace JOIN.Domain.Admin;



/// <summary>
/// Holds specific commercial and business information for Legal persons
/// or independent professionals (B2B profiling).
/// </summary>
public class PersonBusinessProfile : BaseTenantEntity
{
    /// <summary>
    /// Gets the foreign key to the core Person entity.
    /// </summary>
    public Guid PersonId { get; private set; }

    /// <summary>
    /// Gets the foreign key to a catalog of industries or economic sectors.
    /// </summary>
    public Guid IndustryId { get; private set; }

    /// <summary>
    /// Gets the foreign key to a catalog of tax regimes.
    /// </summary>
    public Guid TaxRegimeId { get; private set; }

    /// <summary>
    /// Gets the official corporate website of the business.
    /// </summary>
    public string? Website { get; private set; }

    /// <summary>
    /// Gets the date the company was legally founded or registered.
    /// </summary>
    public DateTime? FoundationDate { get; private set; }

    /// <summary>
    /// Indicates whether this business profile record is active in the system.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Gets whether this profile is the active business profile for its owner
    /// (active flag set and not soft-deleted).
    /// </summary>
    public bool IsCurrentlyActive => IsActive && GcRecord == ActiveGcRecord;

    // --- Navigation Properties ---

    /// <summary>
    /// Reference to the Person entity that owns this business profile.
    /// </summary>
    public virtual Person Person { get; set; } = null!;

    /// <summary>
    /// Navigation to the industry catalog.
    /// </summary>
    public virtual Industry Industry { get; set; } = null!;

    /// <summary>
    /// Navigation to the tax regime catalog.
    /// </summary>
    public virtual TaxRegime TaxRegime { get; set; } = null!;

    // --- Factory & Mutation ---

    /// <summary>
    /// Creates a new active business profile for a person within a tenant.
    /// </summary>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <param name="personId">The person identifier.</param>
    /// <param name="industryId">The industry catalog identifier.</param>
    /// <param name="taxRegimeId">The tax regime catalog identifier.</param>
    /// <param name="website">Optional corporate website.</param>
    /// <param name="foundationDate">Optional foundation date.</param>
    /// <returns>A new <see cref="PersonBusinessProfile"/> ready to be the active profile.</returns>
    public static PersonBusinessProfile Create(
        Guid companyId,
        Guid personId,
        Guid industryId,
        Guid taxRegimeId,
        string? website = null,
        DateTime? foundationDate = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (personId == Guid.Empty)
        {
            throw new ArgumentException("PersonId is required.", nameof(personId));
        }

        if (industryId == Guid.Empty)
        {
            throw new ArgumentException("IndustryId is required.", nameof(industryId));
        }

        if (taxRegimeId == Guid.Empty)
        {
            throw new ArgumentException("TaxRegimeId is required.", nameof(taxRegimeId));
        }

        return new PersonBusinessProfile
        {
            CompanyId = companyId,
            PersonId = personId,
            IndustryId = industryId,
            TaxRegimeId = taxRegimeId,
            Website = website?.Trim(),
            FoundationDate = foundationDate?.Date,
            IsActive = true,
            GcRecord = ActiveGcRecord
        };
    }

    /// <summary>
    /// Updates the editable business profile data.
    /// </summary>
    /// <param name="industryId">The industry catalog identifier.</param>
    /// <param name="taxRegimeId">The tax regime catalog identifier.</param>
    /// <param name="website">Optional corporate website.</param>
    /// <param name="foundationDate">Optional foundation date.</param>
    public void Update(
        Guid industryId,
        Guid taxRegimeId,
        string? website,
        DateTime? foundationDate)
    {
        if (industryId == Guid.Empty)
        {
            throw new ArgumentException("IndustryId is required.", nameof(industryId));
        }

        if (taxRegimeId == Guid.Empty)
        {
            throw new ArgumentException("TaxRegimeId is required.", nameof(taxRegimeId));
        }

        IndustryId = industryId;
        TaxRegimeId = taxRegimeId;
        Website = website?.Trim();
        FoundationDate = foundationDate?.Date;
    }

    // --- Domain Behavior ---

    /// <summary>
    /// Deactivates the business profile in the system.
    /// This action is heavily restricted at the Application layer.
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
    }

    /// <summary>
    /// Reactivates the business profile in the system.
    /// This action is heavily restricted at the Application layer.
    /// </summary>
    public void Reactivate()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
    }
}
