using FluentValidation;

namespace JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Commands.LinkCustomerToPersonPendingConfirmation;

/// <summary>
/// Validates <see cref="LinkCustomerToPersonPendingConfirmationCommand"/> (SPEC 45).
/// </summary>
public sealed class LinkCustomerToPersonPendingConfirmationCommandValidator : AbstractValidator<LinkCustomerToPersonPendingConfirmationCommand>
{
    public LinkCustomerToPersonPendingConfirmationCommandValidator()
    {
        RuleFor(x => x.PendingConfirmationId).NotEmpty().WithMessage("The pending confirmation id is required.");
        RuleFor(x => x.CustomerId).NotEmpty().WithMessage("customerId is required.");
    }
}
