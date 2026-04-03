using FluentValidation;

namespace JOIN.Application.UseCases.Common.Companies.Commands;

/// <summary>
/// Defines validation rules for <see cref="DeleteCompanyCommand"/>.
/// </summary>
public class DeleteCompanyCommandValidator : AbstractValidator<DeleteCompanyCommand>
{
    public DeleteCompanyCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Company id is required.");
    }
}
