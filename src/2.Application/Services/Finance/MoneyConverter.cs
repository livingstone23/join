using JOIN.Application.Interface.Finance;

namespace JOIN.Application.Services.Finance;

/// <summary>
/// SPEC 56 rule 3: amount × rate rounded to 2 decimals, midpoint away from zero, so every module converts alike.
/// </summary>
public sealed class MoneyConverter : IMoneyConverter
{
    /// <inheritdoc />
    public decimal ToBase(decimal amount, decimal rate) =>
        Math.Round(amount * rate, 2, MidpointRounding.AwayFromZero);
}
