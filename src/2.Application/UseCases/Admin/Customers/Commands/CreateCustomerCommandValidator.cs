


using JOIN.Domain.Enums;
using FluentValidation;



namespace JOIN.Application.UseCases.Admin.Customers.Commands;



/// <summary>
/// Defines the validation rules for the CreateCustomerCommand.
/// Ensures data integrity before the command reaches the handler.
/// </summary>
public class CreateCustomerCommandValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerCommandValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name cannot exceed 100 characters.");

        RuleFor(x => x.MiddleName)
            .MaximumLength(100).WithMessage("Middle name cannot exceed 100 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.MiddleName));

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100).WithMessage("Last name cannot exceed 100 characters.");

        RuleFor(x => x.SecondLastName)
            .MaximumLength(100).WithMessage("Second last name cannot exceed 100 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.SecondLastName));

        RuleFor(x => x.CommercialName)
            .MaximumLength(200).WithMessage("Commercial name cannot exceed 200 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.CommercialName));

        RuleFor(x => x.IdentificationNumber)
            .NotEmpty().WithMessage("Identification number is required.")
            .MaximumLength(50).WithMessage("Identification number cannot exceed 50 characters.");

        RuleFor(x => x.PersonType)
            .NotEmpty().WithMessage("Person type must be specified (e.g., Physical or Legal).")
            .MaximumLength(50).WithMessage("Person type cannot exceed 50 characters.")
            .Must(value => Enum.TryParse<PersonType>(value, true, out _))
            .WithMessage("Person type must be a valid value: Physical or Legal.");

        RuleFor(x => x.IdentificationTypeId)
            .NotEmpty().WithMessage("Identification type is required.");

        RuleFor(x => x.Addresses)
            .Must(addresses => addresses is null || addresses.Count(a => a.IsDefault) <= 1)
            .WithMessage("Only one address can be marked as default.");

        RuleFor(x => x.Contacts)
            .Must(contacts => contacts is null || contacts.Count(c => c.IsPrimary) <= 1)
            .WithMessage("Only one contact can be marked as primary.");

        When(x => x.Addresses is { Count: > 0 }, () =>
        {
            RuleForEach(x => x.Addresses!).SetValidator(new CreateCustomerAddressDtoValidator());
        });

        When(x => x.Contacts is { Count: > 0 }, () =>
        {
            RuleForEach(x => x.Contacts!).SetValidator(new CreateCustomerContactDtoValidator());
        });
    }

    private sealed class CreateCustomerAddressDtoValidator : AbstractValidator<CreateCustomerCommand.CreateCustomerAddressDto>
    {
        public CreateCustomerAddressDtoValidator()
        {
            RuleFor(x => x.AddressLine1)
                .NotEmpty().WithMessage("Address line 1 is required.")
                .MaximumLength(200).WithMessage("Address line 1 cannot exceed 200 characters.");

            RuleFor(x => x.AddressLine2)
                .MaximumLength(200).WithMessage("Address line 2 cannot exceed 200 characters.")
                .When(x => !string.IsNullOrWhiteSpace(x.AddressLine2));

            RuleFor(x => x.ZipCode)
                .NotEmpty().WithMessage("Zip code is required.")
                .MaximumLength(20).WithMessage("Zip code cannot exceed 20 characters.");

            RuleFor(x => x.StreetTypeId)
                .NotEmpty().WithMessage("Street type is required.");

            RuleFor(x => x.CountryId)
                .NotEmpty().WithMessage("Country is required.");

            RuleFor(x => x.ProvinceId)
                .NotEmpty().WithMessage("Province is required.");

            RuleFor(x => x.MunicipalityId)
                .NotEmpty().WithMessage("Municipality is required.");
        }
    }

    private sealed class CreateCustomerContactDtoValidator : AbstractValidator<CreateCustomerCommand.CreateCustomerContactDto>
    {
        public CreateCustomerContactDtoValidator()
        {
            RuleFor(x => x.ContactType)
                .NotEmpty().WithMessage("Contact type is required.")
                .MaximumLength(50).WithMessage("Contact type cannot exceed 50 characters.")
                .Must(value => Enum.TryParse<ContactType>(value, true, out _))
                .WithMessage("Contact type must be a valid value.");

            RuleFor(x => x.ContactValue)
                .NotEmpty().WithMessage("Contact value is required.")
                .MaximumLength(150).WithMessage("Contact value cannot exceed 150 characters.");

            RuleFor(x => x.Comments)
                .MaximumLength(500).WithMessage("Comments cannot exceed 500 characters.")
                .When(x => !string.IsNullOrWhiteSpace(x.Comments));
        }
    }
}
