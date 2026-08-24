using FluentValidation;
using JOIN.Application.UseCases.Security.Users.Commands.ChangeUserStatus;

namespace JOIN.Application.UseCases.Security.Users.Commands.ChangeUserStatus;

/// <summary>
/// Validates <see cref="ChangeUserStatusCommand"/>: target user must exist, reason must
/// be present and within the column length.
/// </summary>
public sealed class ChangeUserStatusCommandValidator : AbstractValidator<ChangeUserStatusCommand>
{
    public ChangeUserStatusCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEqual(Guid.Empty);

        RuleFor(x => x.Reason)
            .NotEmpty()
            .MaximumLength(500);
    }
}
