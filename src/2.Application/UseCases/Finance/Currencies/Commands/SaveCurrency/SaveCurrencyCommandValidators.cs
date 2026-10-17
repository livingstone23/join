using FluentValidation;

namespace JOIN.Application.UseCases.Finance.Currencies.Commands;

/// <summary>
/// Rules shared by <see cref="CreateCurrencyCommand"/> and <see cref="UpdateCurrencyCommand"/>.
/// </summary>
internal static class CurrencyInputRules
{
    internal static void Apply<T>(AbstractValidator<T> validator) where T : ICurrencyInput
    {
        validator.RuleFor(x => x.Code)
            .NotEmpty()
            .Must(code => code.Trim().Length == 3 && code.Trim().All(char.IsLetter))
            .WithMessage("Code must be a three-letter ISO 4217 code.");

        validator.RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        validator.RuleFor(x => x.Symbol).NotEmpty().MaximumLength(10);
        validator.RuleFor(x => x.Decimals).InclusiveBetween(0, 4);
    }
}

/// <summary>
/// Validates <see cref="CreateCurrencyCommand"/>.
/// </summary>
public sealed class CreateCurrencyCommandValidator : AbstractValidator<CreateCurrencyCommand>
{
    public CreateCurrencyCommandValidator() => CurrencyInputRules.Apply(this);
}

/// <summary>
/// Validates <see cref="UpdateCurrencyCommand"/>.
/// </summary>
public sealed class UpdateCurrencyCommandValidator : AbstractValidator<UpdateCurrencyCommand>
{
    public UpdateCurrencyCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        CurrencyInputRules.Apply(this);
    }
}
