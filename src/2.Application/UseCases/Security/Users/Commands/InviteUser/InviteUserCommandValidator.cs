using FluentValidation;
using JOIN.Application.UseCases.Security.Users.Commands.InviteUser;

namespace JOIN.Application.UseCases.Security.Users.Commands.InviteUser;

/// <summary>
/// Validates <see cref="InviteUserCommand"/>: email format, names not empty, roleIds
/// non-empty + size cap + no duplicates + each id non-empty.
/// </summary>
public sealed class InviteUserCommandValidator : AbstractValidator<InviteUserCommand>
{
    /// <summary>Hard cap on roles per invitation. Covers any realistic case while bounding payload size.</summary>
    public const int MaxRolesPerInvite = 20;

    public InviteUserCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.FirstName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.LastName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.RoleIds)
            .NotNull()
            .Must(ids => ids != null && ids.Count > 0)
            .WithMessage("At least one role is required.");

        RuleFor(x => x.RoleIds)
            .Must(ids => ids == null || ids.Count <= MaxRolesPerInvite)
            .WithMessage($"Up to {MaxRolesPerInvite} roles per invitation are allowed.")
            .When(x => x.RoleIds != null);

        RuleFor(x => x.RoleIds)
            .Must(ids => ids == null || ids.Distinct().Count() == ids.Count)
            .WithMessage("Duplicate role ids are not allowed.")
            .When(x => x.RoleIds != null);

        RuleForEach(x => x.RoleIds)
            .NotEqual(Guid.Empty)
            .When(x => x.RoleIds != null);
    }
}
