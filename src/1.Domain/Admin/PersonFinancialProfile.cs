using JOIN.Domain.Audit;



namespace JOIN.Domain.Admin;



/// <summary>
/// Represents the economic capacity and declared income of a Person.
/// Essential for product offerings, credit scoring, or KYC (Know Your Customer) compliance.
/// </summary>
public class PersonFinancialProfile : BaseTenantEntity
{
    /// <summary>
    /// Gets the foreign key to the core Person entity.
    /// </summary>
    public Guid PersonId { get; private set; }

    /// <summary>
    /// Gets the foreign key to a catalog of income ranges.
    /// </summary>
    public Guid IncomeRangeId { get; private set; }

    /// <summary>
    /// Gets the source of the income (e.g., "Salary", "Investments", "Business Owner").
    /// </summary>
    public string SourceOfFunds { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the date when the customer declared this financial information.
    /// </summary>
    public DateTime DeclaredDate { get; private set; }

    /// <summary>
    /// Indicates if this is the most recent and valid financial profile.
    /// </summary>
    public bool IsCurrent { get; private set; }

    /// <summary>
    /// Indicates whether this financial profile record is active in the system.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Gets whether the profile is eligible to be marked as current (active and not soft-deleted).
    /// </summary>
    public bool CanBeCurrent => IsActive && GcRecord == ActiveGcRecord;

    // --- Navigation Properties ---

    /// <summary>
    /// Reference to the Person entity that owns this financial profile.
    /// </summary>
    public virtual Person Person { get; set; } = null!;

    /// <summary>
    /// Navigation to the income range catalog.
    /// </summary>
    public virtual IncomeRange IncomeRange { get; set; } = null!;

    // --- Factory & Mutation ---

    /// <summary>
    /// Creates a new active financial profile for a person within a tenant.
    /// </summary>
    public static PersonFinancialProfile Create(
        Guid companyId,
        Guid personId,
        Guid incomeRangeId,
        string sourceOfFunds,
        DateTime declaredDate)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (personId == Guid.Empty)
        {
            throw new ArgumentException("PersonId is required.", nameof(personId));
        }

        if (incomeRangeId == Guid.Empty)
        {
            throw new ArgumentException("IncomeRangeId is required.", nameof(incomeRangeId));
        }

        if (string.IsNullOrWhiteSpace(sourceOfFunds))
        {
            throw new ArgumentException("SourceOfFunds is required.", nameof(sourceOfFunds));
        }

        return new PersonFinancialProfile
        {
            CompanyId = companyId,
            PersonId = personId,
            IncomeRangeId = incomeRangeId,
            SourceOfFunds = sourceOfFunds.Trim(),
            DeclaredDate = declaredDate,
            IsActive = true,
            IsCurrent = false,
            GcRecord = ActiveGcRecord
        };
    }

    /// <summary>
    /// Updates the editable financial profile data.
    /// </summary>
    public void Update(Guid incomeRangeId, string sourceOfFunds, DateTime declaredDate)
    {
        if (incomeRangeId == Guid.Empty)
        {
            throw new ArgumentException("IncomeRangeId is required.", nameof(incomeRangeId));
        }

        if (string.IsNullOrWhiteSpace(sourceOfFunds))
        {
            throw new ArgumentException("SourceOfFunds is required.", nameof(sourceOfFunds));
        }

        IncomeRangeId = incomeRangeId;
        SourceOfFunds = sourceOfFunds.Trim();
        DeclaredDate = declaredDate;
    }

    // --- Domain Behavior ---

    /// <summary>
    /// Marks this profile as the current financial profile for its owner.
    /// </summary>
    public void SetAsCurrent()
    {
        if (!CanBeCurrent)
        {
            throw new InvalidOperationException("Only an active financial profile can be marked as current.");
        }

        IsCurrent = true;
    }

    /// <summary>
    /// Archives this financial profile (clears the current flag).
    /// </summary>
    public void Archive()
    {
        if (!IsCurrent)
        {
            return;
        }

        IsCurrent = false;
    }

    /// <summary>
    /// Deactivates the financial profile and archives it.
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Archive();
    }

    /// <summary>
    /// Reactivates the financial profile in the system.
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
