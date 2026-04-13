using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TicketComplexities.Commands;

/// <summary>
/// Defines validation rules for <see cref="DeleteTicketComplexityCommand"/>.
/// </summary>
public sealed class DeleteTicketComplexityCommandValidator : AbstractValidator<DeleteTicketComplexityCommand>
{
    public DeleteTicketComplexityCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Ticket complexity id is required.");
    }
}
