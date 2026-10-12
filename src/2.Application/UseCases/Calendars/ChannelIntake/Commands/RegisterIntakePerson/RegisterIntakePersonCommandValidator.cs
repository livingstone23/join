using FluentValidation;
using JOIN.Domain.Common;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake;

/// <summary>
/// Validates <see cref="RegisterIntakePersonCommand"/>. Mirrors <c>CreatePersonCommandValidator</c> but with
/// <c>GenderId</c> optional (channel-agent registrations skip it), strict E.164 for <c>Phone</c>, a
/// mandatory <c>Reason</c>, and at least one of <c>Phone</c> or <c>Email</c> (SPEC 45 F4).
/// </summary>
public sealed class RegisterIntakePersonCommandValidator : AbstractValidator<RegisterIntakePersonCommand>
{
    public RegisterIntakePersonCommandValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty().WithMessage("The X-Company-Id header is required.");
        RuleFor(x => x.OriginChannelCode).NotEmpty().WithMessage("The origin channel code is required.")
            .Must(c => c.Equals(CommunicationChannelCodes.WhatsApp, StringComparison.OrdinalIgnoreCase)
                || c.Equals(CommunicationChannelCodes.Twilio, StringComparison.OrdinalIgnoreCase)
                || c.Equals(CommunicationChannelCodes.Telegram, StringComparison.OrdinalIgnoreCase)
                || c.Equals(CommunicationChannelCodes.SendGrid, StringComparison.OrdinalIgnoreCase)
                || c.Equals(CommunicationChannelCodes.App, StringComparison.OrdinalIgnoreCase))
            .WithMessage("The origin channel code must be one of WHATSAPP, TWILIO, TELEGRAM, SENDGRID or APP.");
        RuleFor(x => x.OriginIdentifier).NotEmpty().MaximumLength(200)
            .WithMessage("The origin identifier is required (max 200 characters).");
        RuleFor(x => x.OriginReference).NotEmpty().MaximumLength(200)
            .WithMessage("The origin reference is required (max 200 characters).");

        RuleFor(x => x.PersonType)
            .Must(v => Enum.IsDefined(typeof(JOIN.Domain.Enums.PersonType), v))
            .WithMessage("The person type must be Physical (1) or Legal (2).");
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100).WithMessage("First name is required (max 100 characters).");
        RuleFor(x => x.LastName).MaximumLength(100).When(x => x.LastName is not null)
            .WithMessage("Last name cannot exceed 100 characters.");
        RuleFor(x => x.CommercialName).MaximumLength(200).When(x => x.CommercialName is not null)
            .WithMessage("Commercial name cannot exceed 200 characters.");
        RuleFor(x => x.IdentificationTypeId).NotEmpty().WithMessage("Identification type is required.");
        RuleFor(x => x.IdentificationNumber).NotEmpty().MaximumLength(50)
            .WithMessage("Identification number is required (max 50 characters).");

        RuleFor(x => x.Phone)
            .MaximumLength(50).When(x => x.Phone is not null)
            .WithMessage("Phone cannot exceed 50 characters.")
            .Matches("^\\+[1-9]\\d{7,14}$").When(x => x.Phone is not null)
            .WithMessage("Phone must be in E.164 format (e.g. +50588887777).");

        RuleFor(x => x.Email)
            .MaximumLength(150).When(x => x.Email is not null)
            .WithMessage("Email cannot exceed 150 characters.")
            .EmailAddress().When(x => x.Email is not null)
            .WithMessage("Email must be a valid e-mail address.");

        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500)
            .WithMessage("Reason is required (max 500 characters).");

        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Phone) || !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("At least one of Phone or Email must be provided.")
            .OverridePropertyName(nameof(RegisterIntakePersonCommand.Phone));
    }
}