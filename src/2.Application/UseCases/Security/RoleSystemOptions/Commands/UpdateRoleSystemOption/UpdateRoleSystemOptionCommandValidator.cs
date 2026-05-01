using FluentValidation;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

/// <summary>
/// Validates the payload used to update a RoleSystemOption rule.
/// </summary>
public sealed class UpdateRoleSystemOptionCommandValidator : AbstractValidator<UpdateRoleSystemOptionCommand>
{
    public UpdateRoleSystemOptionCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");

        RuleFor(x => x.CompanyId)
            .NotEmpty()
            .WithMessage("CompanyId is required.");
    }
}
