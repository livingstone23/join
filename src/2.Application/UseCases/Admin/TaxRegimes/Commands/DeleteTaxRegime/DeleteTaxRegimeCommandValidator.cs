using FluentValidation;

namespace JOIN.Application.UseCases.Admin.TaxRegimes.Commands;

public sealed class DeleteTaxRegimeCommandValidator : AbstractValidator<DeleteTaxRegimeCommand>
{
    public DeleteTaxRegimeCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}
