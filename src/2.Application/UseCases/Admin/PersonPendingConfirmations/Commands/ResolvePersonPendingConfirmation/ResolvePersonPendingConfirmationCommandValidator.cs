using FluentValidation;
using JOIN.Domain.Enums;

namespace JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Commands.ResolvePersonPendingConfirmation;

/// <summary>
/// Validates <see cref="ResolvePersonPendingConfirmationCommand"/> (SPEC 45).
/// </summary>
public sealed class ResolvePersonPendingConfirmationCommandValidator : AbstractValidator<ResolvePersonPendingConfirmationCommand>
{
    public ResolvePersonPendingConfirmationCommandValidator()
    {
        RuleFor(x => x.PendingConfirmationId).NotEmpty().WithMessage("The pending confirmation id is required.");
        RuleFor(x => x.Status)
            .Must(status => status is PersonPendingConfirmationStatus.Confirmed or PersonPendingConfirmationStatus.Discarded)
            .WithMessage("status must be Confirmed or Discarded.");
        RuleFor(x => x.Notes)
            .NotEmpty().When(x => x.Status == PersonPendingConfirmationStatus.Discarded)
            .WithMessage("notes are required to discard a pending confirmation.")
            .MaximumLength(1000).WithMessage("notes cannot exceed 1000 characters.");
    }
}
