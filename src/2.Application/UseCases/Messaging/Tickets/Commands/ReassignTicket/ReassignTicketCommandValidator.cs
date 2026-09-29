using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands.ReassignTicket;

/// <summary>
/// Defines validation rules for <see cref="ReassignTicketCommand"/>.
/// </summary>
public sealed class ReassignTicketCommandValidator : AbstractValidator<ReassignTicketCommand>
{
    public ReassignTicketCommandValidator()
    {
        RuleFor(x => x.TicketId)
            .NotEqual(Guid.Empty)
            .WithMessage("Ticket id is required.");

        RuleFor(x => x.NewAssignedToUserId)
            .NotEqual(Guid.Empty)
            .WithMessage("New assigned user id is required.");
    }
}
