using FluentValidation;

namespace JOIN.Application.UseCases.Security.Users.Commands.ReplaceUserRoles;

/// <summary>
/// Validates the payload used to replace the roles assigned to a user.
/// </summary>
public sealed class ReplaceUserRolesCommandValidator : AbstractValidator<ReplaceUserRolesCommand>
{
    public ReplaceUserRolesCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("UserId is required.");

        RuleFor(x => x.Roles)
            .NotNull()
            .WithMessage("Roles collection is required.");
    }
}
