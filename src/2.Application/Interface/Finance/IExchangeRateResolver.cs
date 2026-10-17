namespace JOIN.Application.Interface.Finance;

/// <summary>
/// Resolves the rate that applies to a company currency on a date (SPEC 56 rule 4).
/// </summary>
public interface IExchangeRateResolver
{
    /// <summary>
    /// Base currency → rate 1. Parallel currency → the rate of <paramref name="date"/> or the latest earlier one.
    /// <c>null</c> when the currency is not an active currency of the company, or has no rate on or before the date.
    /// </summary>
    Task<ExchangeRateResolution?> ResolveAsync(Guid companyId, Guid currencyId, DateOnly date, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of <see cref="IExchangeRateResolver.ResolveAsync"/>. <see cref="RateDate"/> is <c>null</c> for the base.
/// </summary>
public sealed record ExchangeRateResolution(decimal Rate, DateOnly? RateDate, bool IsBase);
