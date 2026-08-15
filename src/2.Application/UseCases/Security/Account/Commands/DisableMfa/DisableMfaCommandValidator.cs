using FluentValidation;

namespace JOIN.Application.UseCases.Security.Account.Commands.DisableMfa;

/// <summary>
/// Validates the inbound <see cref="DisableMfaCommand"/>'s code is non-empty.
/// </summary>
public sealed class DisableMfaCommandValidator : AbstractValidator<DisableMfaCommand>
{
    /// <summary>
    /// Builds the validator.
    /// </summary>
    public DisableMfaCommandValidator()
    {
        RuleFor(command => command.Code)
            .NotEmpty()
            .WithErrorCode("MFA_INVALID_CODE")
            .WithMessage("A TOTP or recovery code must be supplied.");
    }
}
