using FluentValidation;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

/// <summary>
/// Validates the payload used to delete a RoleSystemOption rule.
/// </summary>
public sealed class DeleteRoleSystemOptionCommandValidator : AbstractValidator<DeleteRoleSystemOptionCommand>
{
    public DeleteRoleSystemOptionCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");

        RuleFor(x => x.CompanyId)
            .NotEmpty()
            .WithMessage("CompanyId is required.");
    }
}
