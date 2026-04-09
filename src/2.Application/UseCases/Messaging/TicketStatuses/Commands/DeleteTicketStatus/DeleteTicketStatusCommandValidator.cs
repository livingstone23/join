using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TicketStatuses.Commands;

/// <summary>
/// Defines validation rules for <see cref="DeleteTicketStatusCommand"/>.
/// </summary>
public sealed class DeleteTicketStatusCommandValidator : AbstractValidator<DeleteTicketStatusCommand>
{
    public DeleteTicketStatusCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Ticket status id is required.");
    }
}
