using JOIN.Domain.Audit;



namespace JOIN.Domain.Admin;



/// <summary>
/// Represents a tenant-specific catalog of income brackets.
/// Used for demographic segmentation and credit scoring during onboarding.
/// </summary>
public class IncomeRange : BaseTenantEntity
{
    

    /// <summary>
    /// The display string for the UI (e.g., "$1,000 - $5,000 USD").
    /// </summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>
    /// The mathematical minimum value of this bracket. Useful for analytical queries.
    /// </summary>
    public decimal MinimumValue { get; private set; }

    /// <summary>
    /// The mathematical maximum value of this bracket. Can be null for "Above X" ranges.
    /// </summary>
    public decimal? MaximumValue { get; private set; }

    /// <summary>
    /// The currency of this specific bracket.
    /// </summary>
    public string CurrencyCode { get; private set; } = "USD";

    public bool IsActive { get; private set; } = true;

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