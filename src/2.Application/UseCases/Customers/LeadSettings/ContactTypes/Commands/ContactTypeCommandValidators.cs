using FluentValidation;
using JOIN.Domain.Customers.Leads;

namespace JOIN.Application.UseCases.Customers.LeadSettings.ContactTypes.Commands;

internal static class ContactTypeInputRules
{
    internal static void Apply<T>(AbstractValidator<T> validator) where T : IContactTypeInput
    {
        validator.RuleFor(x => x.Code)
            .Must(code => !string.IsNullOrWhiteSpace(code)).WithMessage("Code is required.")
            .Must(code => CommercialCode.Normalize(code ?? string.Empty).Length <= 30).WithMessage("Code cannot exceed 30 characters.");
        validator.RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        validator.RuleFor(x => x.Icon).NotEmpty().MaximumLength(50);
        validator.RuleFor(x => x.CommunicationChannelCode).MaximumLength(30);
    }
}

/// <summary>Validates <see cref="CreateContactTypeCommand"/>.</summary>
public sealed class CreateContactTypeCommandValidator : AbstractValidator<CreateContactTypeCommand>
{
    public CreateContactTypeCommandValidator() => ContactTypeInputRules.Apply(this);
}

/// <summary>Validates <see cref="UpdateContactTypeCommand"/>.</summary>
public sealed class UpdateContactTypeCommandValidator : AbstractValidator<UpdateContactTypeCommand>
{
    public UpdateContactTypeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        ContactTypeInputRules.Apply(this);
    }
}
