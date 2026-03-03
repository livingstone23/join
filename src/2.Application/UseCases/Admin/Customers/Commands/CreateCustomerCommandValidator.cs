


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

            RuleFor(x => x.CustomerDto.IdentificationNumber)
                .NotEmpty().WithMessage("Identification number is required.")
                .MaximumLength(50).WithMessage("Identification number cannot exceed 50 characters.");

            RuleFor(x => x.CustomerDto.PersonType)
                .NotEmpty().WithMessage("Person type must be specified (e.g., Physical or Legal).");

            RuleFor(x => x.CustomerDto.CompanyId)
                .NotEmpty().WithMessage("Company identifier (Tenant) is strictly required.");
        });
    }
}
