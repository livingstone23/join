using FluentValidation;

namespace JOIN.Application.UseCases.Admin.TaxRegimes.Commands;

public sealed class UpdateTaxRegimeCommandValidator : AbstractValidator<UpdateTaxRegimeCommand>
{
    public UpdateTaxRegimeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
    }
}
