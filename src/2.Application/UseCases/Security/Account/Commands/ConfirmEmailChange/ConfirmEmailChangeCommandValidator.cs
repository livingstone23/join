using FluentValidation;

namespace JOIN.Application.UseCases.Security.Account.Commands.ConfirmEmailChange;

/// <summary>
/// Validates the inbound <see cref="ConfirmEmailChangeCommand"/>'s shape. The new email
/// must look like an email address; the token must be non-empty. Identity itself rejects
/// mismatched new-email/token pairs at runtime.
/// </summary>
public sealed class ConfirmEmailChangeCommandValidator : AbstractValidator<ConfirmEmailChangeCommand>
{
    /// <summary>
    /// Builds the validator with the email-shape constraint.
    /// </summary>
    public ConfirmEmailChangeCommandValidator()
    {
        RuleFor(command => command.NewEmail)
            .NotEmpty()
            .EmailAddress()
            .WithErrorCode("EMAIL_CHANGE_FAILED")
            .WithMessage("A valid email address must be supplied.");

        RuleFor(command => command.Token)
            .NotEmpty()
            .WithErrorCode("EMAIL_CHANGE_FAILED")
            .WithMessage("The confirmation token must be supplied.");
    }
}
