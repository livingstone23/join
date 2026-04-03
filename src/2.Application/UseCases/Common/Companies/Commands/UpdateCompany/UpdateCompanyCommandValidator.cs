using FluentValidation;

namespace JOIN.Application.UseCases.Common.Companies.Commands;

/// <summary>
/// Defines validation rules for <see cref="UpdateCompanyCommand"/>.
/// </summary>
public class UpdateCompanyCommandValidator : AbstractValidator<UpdateCompanyCommand>
{
    public UpdateCompanyCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Company id is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Company name is required.")
            .MaximumLength(150).WithMessage("Company name cannot exceed 150 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Description));

        RuleFor(x => x.TaxId)
            .NotEmpty().WithMessage("Tax identifier is required.")
            .MaximumLength(50).WithMessage("Tax identifier cannot exceed 50 characters.");

        RuleFor(x => x.Email)
            .MaximumLength(100).WithMessage("Email cannot exceed 100 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.Phone)
            .MaximumLength(50).WithMessage("Phone cannot exceed 50 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));

        RuleFor(x => x.WebSite)
            .MaximumLength(200).WithMessage("Website cannot exceed 200 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.WebSite));
    }
}
