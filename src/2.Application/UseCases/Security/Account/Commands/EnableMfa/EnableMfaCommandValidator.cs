using FluentValidation;

namespace JOIN.Application.UseCases.Security.Account.Commands.EnableMfa;

/// <summary>
/// Validates the inbound <see cref="EnableMfaCommand"/>'s TOTP code shape. The handler
/// produces a richer set of error codes (MFA_NOT_CONFIGURED / MFA_INVALID_CODE) on top
/// of the bare-shape rejection here.
/// </summary>
public sealed class EnableMfaCommandValidator : AbstractValidator<EnableMfaCommand>
{
    /// <summary>
    /// Builds the validator with the TOTP shape constraint.
    /// </summary>
    public EnableMfaCommandValidator()
    {
        RuleFor(command => command.Code)
            .NotEmpty()
            .Matches("^[0-9]{6}$")
            .WithErrorCode("MFA_INVALID_CODE")
            .WithMessage("The supplied MFA code must be a 6-digit numeric value.");
    }
}
