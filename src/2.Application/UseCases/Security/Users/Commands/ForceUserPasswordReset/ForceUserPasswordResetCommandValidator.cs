using FluentValidation;
using JOIN.Application.UseCases.Security.Users.Commands.ForceUserPasswordReset;

namespace JOIN.Application.UseCases.Security.Users.Commands.ForceUserPasswordReset;

/// <summary>
/// Validates <see cref="ForceUserPasswordResetCommand"/>: target user required; reason
/// is optional but bounded by the column length when present.
/// </summary>
public sealed class ForceUserPasswordResetCommandValidator : AbstractValidator<ForceUserPasswordResetCommand>
{
    public ForceUserPasswordResetCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEqual(Guid.Empty);

        RuleFor(x => x.Reason)
            .MaximumLength(500)
            .When(x => x.Reason != null);
    }
}
