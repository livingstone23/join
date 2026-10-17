using JOIN.Application.Interface.Finance;

namespace JOIN.Application.UseCases.Finance.CompanyCurrencies;

/// <summary>
/// SPEC 56 rule 6: the base currency cannot change once the company stores amounts. Asks every registered
/// <see cref="IBaseCurrencyAmountSource"/>; Finance does not know which modules store amounts.
/// </summary>
public sealed class CompanyBaseCurrencyChangeGuard(IEnumerable<IBaseCurrencyAmountSource> sources)
{
    /// <summary>Names of the sources that report amounts for the company (empty when the base can change).</summary>
    public async Task<IReadOnlyList<string>> GetSourcesWithAmountsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var withAmounts = new List<string>();
        foreach (var source in sources)
        {
            if (await source.HasAmountsAsync(companyId, cancellationToken))
            {
                withAmounts.Add(source.Name);
            }
        }

        return withAmounts;
    }
}
