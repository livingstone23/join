using FluentValidation;

namespace JOIN.Application.UseCases.Security.UserCompanies.Commands.RemoveUserCompany;

/// <summary>
/// Validates <see cref="RemoveUserCompanyCommand"/>: both identifiers must be
/// non-empty. Business guards (last-company / default-company) live in the handler
/// because they require a DB read and surface their own error codes
/// (<c>CANNOT_REMOVE_LAST_COMPANY</c>, <c>CANNOT_REMOVE_DEFAULT_COMPANY</c>).
/// </summary>
public sealed class RemoveUserCompanyCommandValidator : AbstractValidator<RemoveUserCompanyCommand>
{
    public RemoveUserCompanyCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("UserId is required.");

        RuleFor(x => x.CompanyId)
            .NotEmpty()
            .WithMessage("CompanyId is required.");
    }
}