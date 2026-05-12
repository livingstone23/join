using FluentValidation;

namespace JOIN.Application.UseCases.Admin.CustomerContacts.Commands;

/// <summary>
/// Validation rules for <see cref="CreateCustomerContactCommand"/>.
/// </summary>
public sealed class CreateCustomerContactValidator : AbstractValidator<CreateCustomerContactCommand>
{
    /// <summary>
    /// Initializes validator rules for creating customer contacts.
    /// </summary>
    public CreateCustomerContactValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty().WithMessage("Customer id is required.");

        RuleFor(x => x.ContactType)
            .IsInEnum().WithMessage("A valid contact type is required.");

        RuleFor(x => x.ContactValue)
            .NotEmpty().WithMessage("Contact value is required.")
            .MaximumLength(200).WithMessage("Contact value cannot exceed 200 characters.");

        RuleFor(x => x.Comments)
            .MaximumLength(500).WithMessage("Comments cannot exceed 500 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Comments));
    }
}
