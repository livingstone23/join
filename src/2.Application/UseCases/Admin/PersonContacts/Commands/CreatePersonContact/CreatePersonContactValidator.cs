using FluentValidation;



namespace JOIN.Application.UseCases.Admin.PersonContacts.Commands;



/// <summary>
/// Validation rules for <see cref="CreatePersonContactCommand"/>.
/// </summary>
public sealed class CreatePersonContactValidator : AbstractValidator<CreatePersonContactCommand>
{
    

    /// <summary>
    /// Initializes validator rules for creating person contacts.
    /// </summary>
    public CreatePersonContactValidator()
    {
        RuleFor(x => x.PersonId)
            .NotEmpty().WithMessage("Person id is required.");

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
