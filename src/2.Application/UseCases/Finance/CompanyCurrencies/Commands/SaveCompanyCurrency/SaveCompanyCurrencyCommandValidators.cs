using FluentValidation;

namespace JOIN.Application.UseCases.Finance.CompanyCurrencies.Commands;

/// <summary>
/// Validates <see cref="CreateCompanyCurrencyCommand"/>.
/// </summary>
public sealed class CreateCompanyCurrencyCommandValidator : AbstractValidator<CreateCompanyCurrencyCommand>
{
    public CreateCompanyCurrencyCommandValidator()
    {
        RuleFor(x => x.CurrencyId).NotEmpty();
    }
}

/// <summary>
/// Validates <see cref="UpdateCompanyCurrencyCommand"/>.
/// </summary>
public sealed class UpdateCompanyCurrencyCommandValidator : AbstractValidator<UpdateCompanyCurrencyCommand>
{
    public UpdateCompanyCurrencyCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
