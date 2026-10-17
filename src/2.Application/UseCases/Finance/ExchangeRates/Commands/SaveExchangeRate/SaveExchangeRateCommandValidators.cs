using FluentValidation;

namespace JOIN.Application.UseCases.Finance.ExchangeRates.Commands;

/// <summary>
/// Rules shared by <see cref="CreateExchangeRateCommand"/> and <see cref="UpdateExchangeRateCommand"/>.
/// </summary>
internal static class ExchangeRateInputRules
{
    internal static void Apply<T>(AbstractValidator<T> validator) where T : IExchangeRateInput
    {
        validator.RuleFor(x => x.CurrencyId).NotEmpty();
        validator.RuleFor(x => x.RateDate).NotEmpty();
        validator.RuleFor(x => x.Rate)
            .GreaterThan(0)
            .WithMessage("Rate must be greater than zero.")
            .PrecisionScale(18, 6, ignoreTrailingZeros: true)
            .WithMessage("Rate allows up to 6 decimals.");
    }
}

/// <summary>
/// Validates <see cref="CreateExchangeRateCommand"/>.
/// </summary>
public sealed class CreateExchangeRateCommandValidator : AbstractValidator<CreateExchangeRateCommand>
{
    public CreateExchangeRateCommandValidator() => ExchangeRateInputRules.Apply(this);
}

/// <summary>
/// Validates <see cref="UpdateExchangeRateCommand"/>.
/// </summary>
public sealed class UpdateExchangeRateCommandValidator : AbstractValidator<UpdateExchangeRateCommand>
{
    public UpdateExchangeRateCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        ExchangeRateInputRules.Apply(this);
    }
}
