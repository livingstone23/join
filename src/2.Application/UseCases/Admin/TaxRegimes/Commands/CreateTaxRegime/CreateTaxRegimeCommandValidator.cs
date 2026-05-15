using FluentValidation;

namespace JOIN.Application.UseCases.Admin.TaxRegimes.Commands;

public sealed class CreateTaxRegimeCommandValidator : AbstractValidator<CreateTaxRegimeCommand>
{
    public CreateTaxRegimeCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
    }
}
