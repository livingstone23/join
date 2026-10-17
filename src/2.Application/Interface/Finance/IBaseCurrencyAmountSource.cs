namespace JOIN.Application.Interface.Finance;

/// <summary>
/// A module that stores amounts in a company's base currency (SPEC 56 rule 6). While any registered source reports
/// amounts, the company's base currency cannot change. SPEC 57 (product reference price) and SPEC 61 (lead amounts)
/// register their own implementation; Finance does not depend on them.
/// </summary>
public interface IBaseCurrencyAmountSource
{
    /// <summary>Name shown in the error detail (for example <c>"Products"</c>).</summary>
    string Name { get; }

    /// <summary><c>true</c> when the company has at least one live amount stored by this source.</summary>
    Task<bool> HasAmountsAsync(Guid companyId, CancellationToken cancellationToken = default);
}
