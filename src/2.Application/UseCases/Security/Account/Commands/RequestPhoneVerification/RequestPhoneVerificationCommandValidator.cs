using FluentValidation;

namespace JOIN.Application.UseCases.Security.Account.Commands.RequestPhoneVerification;

/// <summary>
/// Validates the inbound <see cref="RequestPhoneVerificationCommand"/>'s E.164 phone.
/// </summary>
public sealed class RequestPhoneVerificationCommandValidator : AbstractValidator<RequestPhoneVerificationCommand>
{
    /// <summary>
    /// Builds the validator with the E.164 regex.
    /// </summary>
    public RequestPhoneVerificationCommandValidator()
    {
        RuleFor(command => command.PhoneNumber)
            .NotEmpty()
            .Matches(@"^\+[1-9]\d{1,14}$")
            .WithErrorCode("PHONE_INVALID_FORMAT")
            .WithMessage("The phone number must be in E.164 format (+[country code]...).");
    }
}
