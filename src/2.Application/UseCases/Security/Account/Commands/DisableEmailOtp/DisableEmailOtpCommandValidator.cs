using FluentValidation;

namespace JOIN.Application.UseCases.Security.Account.Commands.DisableEmailOtp;

/// <summary>
/// Validates the inbound <see cref="DisableEmailOtpCommand"/>'s code shape. The handler produces
/// a richer set of error codes (EMAIL_OTP_NOT_REQUESTED / EMAIL_OTP_CODE_EXPIRED /
/// EMAIL_OTP_CODE_INVALID / EMAIL_OTP_CODE_LOCKED) on top of the bare-shape rejection here.
/// </summary>
public sealed class DisableEmailOtpCommandValidator : AbstractValidator<DisableEmailOtpCommand>
{
    public DisableEmailOtpCommandValidator()
    {
        RuleFor(command => command.Code)
            .NotEmpty()
            .Matches("^[0-9]{6}$")
            .WithErrorCode("EMAIL_OTP_CODE_INVALID")
            .WithMessage("The supplied code must be a 6-digit numeric value.");
    }
}
