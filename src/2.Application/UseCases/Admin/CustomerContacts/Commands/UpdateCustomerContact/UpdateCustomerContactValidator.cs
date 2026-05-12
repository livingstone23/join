using FluentValidation;

namespace JOIN.Application.UseCases.Admin.CustomerContacts.Commands;

/// <summary>
/// Validation rules for <see cref="UpdateCustomerContactCommand"/>.
/// </summary>
public sealed class UpdateCustomerContactValidator : AbstractValidator<UpdateCustomerContactCommand>
{
    /// <summary>
    /// Initializes validator rules for updating customer contacts.
    /// </summary>
    public UpdateCustomerContactValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Contact id is required.");

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
