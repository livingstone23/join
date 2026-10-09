using FluentValidation;

namespace JOIN.Application.UseCases.Security.UserCompanies.Commands.RestoreUserCompany;

/// <summary>
/// Validates <see cref="RestoreUserCompanyCommand"/>.
/// </summary>
public sealed class RestoreUserCompanyCommandValidator : AbstractValidator<RestoreUserCompanyCommand>
{
    public RestoreUserCompanyCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("UserId is required.");

        RuleFor(x => x.CompanyId)
            .NotEmpty()
            .WithMessage("CompanyId is required.");
    }
}
