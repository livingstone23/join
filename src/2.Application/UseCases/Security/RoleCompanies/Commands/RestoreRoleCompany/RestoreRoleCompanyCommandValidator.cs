using FluentValidation;

namespace JOIN.Application.UseCases.Security.RoleCompanies.Commands.DeleteRoleCompany;

/// <summary>
/// Validates <see cref="RestoreRoleCompanyCommand"/>.
/// </summary>
public sealed class RestoreRoleCompanyCommandValidator : AbstractValidator<RestoreRoleCompanyCommand>
{
    public RestoreRoleCompanyCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
