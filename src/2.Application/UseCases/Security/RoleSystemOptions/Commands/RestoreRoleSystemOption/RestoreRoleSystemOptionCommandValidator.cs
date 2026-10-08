using FluentValidation;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

/// <summary>
/// Validates <see cref="RestoreRoleSystemOptionCommand"/>.
/// </summary>
public sealed class RestoreRoleSystemOptionCommandValidator : AbstractValidator<RestoreRoleSystemOptionCommand>
{
    public RestoreRoleSystemOptionCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
