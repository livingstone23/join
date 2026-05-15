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

    public static IncomeRange Create(
        Guid companyId,
        string displayName,
        decimal minimumValue,
        decimal? maximumValue,
        string currencyCode)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName is required.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(currencyCode))
            throw new ArgumentException("CurrencyCode is required.", nameof(currencyCode));
        if (maximumValue.HasValue && maximumValue.Value < minimumValue)
            throw new ArgumentException("MaximumValue must be greater than or equal to MinimumValue.", nameof(maximumValue));

        return new IncomeRange
        {
            CompanyId = companyId,
            DisplayName = displayName.Trim(),
            MinimumValue = minimumValue,
            MaximumValue = maximumValue,
            CurrencyCode = currencyCode.Trim().ToUpperInvariant(),
            IsActive = true,
            GcRecord = ActiveGcRecord
        };
    }

    public void Update(
        string displayName,
        decimal minimumValue,
        decimal? maximumValue,
        string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName is required.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(currencyCode))
            throw new ArgumentException("CurrencyCode is required.", nameof(currencyCode));
        if (maximumValue.HasValue && maximumValue.Value < minimumValue)
            throw new ArgumentException("MaximumValue must be greater than or equal to MinimumValue.", nameof(maximumValue));

        DisplayName = displayName.Trim();
        MinimumValue = minimumValue;
        MaximumValue = maximumValue;
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
    }

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