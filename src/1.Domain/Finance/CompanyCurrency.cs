using JOIN.Domain.Audit;

namespace JOIN.Domain.Finance;

/// <summary>
/// A currency the company uses (table <c>Finance.CompanyCurrencies</c>, SPEC 56). Exactly one live row per company
/// has <see cref="IsBase"/>: the base currency every amount is summed in. The base has no exchange rate (rate 1).
/// </summary>
public class CompanyCurrency : BaseTenantEntity
{
    public Guid CurrencyId { get; set; }

    /// <summary>The company's base currency. Exactly one live, active row per company.</summary>
    public bool IsBase { get; set; }

    public bool IsActive { get; set; } = true;

    public virtual Currency Currency { get; set; } = null!;
}
