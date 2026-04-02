


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
        // Validates the nested DTO
        RuleFor(x => x.CustomerDto).NotNull().WithMessage("Customer data must be provided.");

        When(x => x.CustomerDto != null, () => 
        {
            RuleFor(x => x.CustomerDto.FirstName)
                .NotEmpty().WithMessage("First name is required.")
                .MaximumLength(100).WithMessage("First name cannot exceed 100 characters.");

            RuleFor(x => x.CustomerDto.MiddleName)
                .MaximumLength(100).WithMessage("Middle name cannot exceed 100 characters.")
                .When(x => !string.IsNullOrWhiteSpace(x.CustomerDto.MiddleName));

            RuleFor(x => x.CustomerDto.LastName)
                .NotEmpty().WithMessage("Last name is required.")
                .MaximumLength(100).WithMessage("Last name cannot exceed 100 characters.");

            RuleFor(x => x.CustomerDto.SecondLastName)
                .MaximumLength(100).WithMessage("Second last name cannot exceed 100 characters.")
                .When(x => !string.IsNullOrWhiteSpace(x.CustomerDto.SecondLastName));

            RuleFor(x => x.CustomerDto.CommercialName)
                .MaximumLength(200).WithMessage("Commercial name cannot exceed 200 characters.")
                .When(x => !string.IsNullOrWhiteSpace(x.CustomerDto.CommercialName));

            RuleFor(x => x.CustomerDto.IdentificationNumber)
                .NotEmpty().WithMessage("Identification number is required.")
                .MaximumLength(50).WithMessage("Identification number cannot exceed 50 characters.");

            RuleFor(x => x.CustomerDto.PersonType)
                .NotEmpty().WithMessage("Person type must be specified (e.g., Physical or Legal).")
                .MaximumLength(50).WithMessage("Person type cannot exceed 50 characters.")
                .Must(value => Enum.TryParse<PersonType>(value, true, out _))
                .WithMessage("Person type must be a valid value: Physical or Legal.");

            RuleFor(x => x.CustomerDto.IdentificationTypeId)
                .NotEmpty().WithMessage("Identification type is required.");
        });
    }
}
