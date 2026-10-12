using FluentValidation;
using JOIN.Domain.Enums;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.RegisterIntakePerson;

/// <summary>
/// Validates <see cref="RegisterIntakePersonCommand"/> (SPEC 45): the length and type rules of
/// <c>CreatePersonCommandValidator</c> except the gender, which is optional for a person created by a channel;
/// <c>phone</c> in E.164, required when the channel is a phone channel; <c>email</c> required when the channel is
/// e-mail; at least one of them; <c>reason</c> required (max 500).
/// </summary>
public sealed class RegisterIntakePersonCommandValidator : AbstractValidator<RegisterIntakePersonCommand>
{
    /// <summary> E.164: a <c>+</c>, a non-zero country digit and 8 to 15 digits in total. </summary>
    public const string E164Pattern = @"^\+[1-9]\d{7,14}$";

    public RegisterIntakePersonCommandValidator()
    {
        RuleFor(x => x.PersonType)
            .Must(value => value is PersonType.Physical or PersonType.Legal)
            .WithMessage("Person type must be Physical (1) or Legal (2).");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name cannot exceed 100 characters.");

        RuleFor(x => x.LastName)
            .NotEmpty().When(x => x.PersonType == PersonType.Physical).WithMessage("Last name is required.");
        RuleFor(x => x.LastName)
            .MaximumLength(100).WithMessage("Last name cannot exceed 100 characters.");

        RuleFor(x => x.CommercialName)
            .NotEmpty().When(x => x.PersonType == PersonType.Legal).WithMessage("Commercial name is required for legal persons.");
        RuleFor(x => x.CommercialName)
            .MaximumLength(200).WithMessage("Commercial name cannot exceed 200 characters.");

        RuleFor(x => x.GenderId)
            .Null().When(x => x.PersonType == PersonType.Legal)
            .WithMessage("Gender id must not be provided for legal persons.");

        RuleFor(x => x.IdentificationTypeId)
            .NotEmpty().WithMessage("Identification type is required.");

        RuleFor(x => x.IdentificationNumber)
            .NotEmpty().WithMessage("Identification number is required.")
            .MaximumLength(50).WithMessage("Identification number cannot exceed 50 characters.");

        // Separate rules: a chained When() would apply to every validator before it.
        RuleFor(x => x.Phone)
            .NotEmpty().When(x => IntakeChannels.IsPhone(x.OriginChannelCode))
            .WithMessage("Phone is required when the channel is a phone channel.");
        RuleFor(x => x.Phone)
            .Matches(E164Pattern).When(x => !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Phone must be in E.164 format (e.g. +50588887777).");

        RuleFor(x => x.Email)
            .NotEmpty().When(x => IntakeChannels.IsEmail(x.OriginChannelCode))
            .WithMessage("Email is required when the channel is e-mail.");
        RuleFor(x => x.Email)
            .MaximumLength(150).WithMessage("Email cannot exceed 150 characters.")
            .EmailAddress().WithMessage("Email must be a valid e-mail address.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Phone) || !string.IsNullOrWhiteSpace(x.Email))
            .WithName(nameof(RegisterIntakePersonCommand.Phone))
            .WithMessage("At least one of phone or email is required.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required.")
            .MaximumLength(500).WithMessage("Reason cannot exceed 500 characters.");

        RuleFor(x => x.OriginChannelCode)
            .NotEmpty().WithMessage("Origin channel code is required.");

        RuleFor(x => x.OriginIdentifier)
            .NotEmpty().WithMessage("Origin identifier is required.")
            .MaximumLength(200).WithMessage("Origin identifier cannot exceed 200 characters.");

        RuleFor(x => x.OriginReference)
            .MaximumLength(200).WithMessage("Origin reference cannot exceed 200 characters.");
    }
}
