using FluentValidation;

namespace JOIN.Application.UseCases.Security.UserCompanies.Commands.SetDefaultCompany;

/// <summary>
/// Validates the input required to set the default company for a user.
/// </summary>
public sealed class SetDefaultCompanyCommandValidator : AbstractValidator<SetDefaultCompanyCommand>
{
    public SetDefaultCompanyCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("UserId is required.");

        RuleFor(x => x.CompanyId)
            .NotEmpty()
            .WithMessage("CompanyId is required.");
    }
}
