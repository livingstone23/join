using FluentValidation;

namespace JOIN.Application.UseCases.Security.Roles.Commands.RestoreRole;

/// <summary>
/// Validates <see cref="RestoreRoleCommand"/>.
/// </summary>
public sealed class RestoreRoleCommandValidator : AbstractValidator<RestoreRoleCommand>
{
    public RestoreRoleCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
