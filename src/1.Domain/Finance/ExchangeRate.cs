using JOIN.Domain.Audit;

namespace JOIN.Domain.Finance;

/// <summary>
/// Dated exchange rate of a parallel currency (table <c>Finance.ExchangeRates</c>, SPEC 56):
/// 1 unit of <see cref="Currency"/> = <see cref="Rate"/> units of the company's base currency.
/// One live row per company, currency and date. The base currency never has rates.
/// </summary>
public class ExchangeRate : BaseTenantEntity
{
    public Guid CurrencyId { get; set; }

    public DateOnly RateDate { get; set; }

    /// <summary>Base-currency units per unit of the currency, 6 decimals, greater than zero.</summary>
    public decimal Rate { get; set; }

    public virtual Currency Currency { get; set; } = null!;
}
