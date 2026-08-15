using FluentValidation;

namespace JOIN.Application.UseCases.Security.Account.Commands.ConfirmPhoneVerification;

/// <summary>
/// Validates the inbound <see cref="ConfirmPhoneVerificationCommand"/>'s code shape.
/// </summary>
public sealed class ConfirmPhoneVerificationCommandValidator : AbstractValidator<ConfirmPhoneVerificationCommand>
{
    /// <summary>
    /// Builds the validator with the 6-digit numeric constraint.
    /// </summary>
    public ConfirmPhoneVerificationCommandValidator()
    {
        RuleFor(command => command.Code)
            .NotEmpty()
            .Matches(@"^\d{6}$")
            .WithErrorCode("PHONE_CODE_INVALID")
            .WithMessage("The verification code must be 6 digits.");
    }
}
