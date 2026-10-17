namespace JOIN.Application.Interface.Finance;

/// <summary>
/// Converts amounts to the base currency with one rounding rule for every module (SPEC 56 rule 3).
/// </summary>
public interface IMoneyConverter
{
    /// <summary><paramref name="amount"/> × <paramref name="rate"/>, rounded to 2 decimals, midpoint away from zero.</summary>
    decimal ToBase(decimal amount, decimal rate);
}
