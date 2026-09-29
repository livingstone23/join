using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands.FinishTicket;

/// <summary>
/// Defines validation rules for <see cref="FinishTicketCommand"/>.
/// </summary>
public sealed class FinishTicketCommandValidator : AbstractValidator<FinishTicketCommand>
{
    public FinishTicketCommandValidator()
    {
        RuleFor(x => x.TicketId)
            .NotEqual(Guid.Empty)
            .WithMessage("Ticket id is required.");

        RuleFor(x => x.TicketStatusId)
            .NotEqual(Guid.Empty)
            .WithMessage("Ticket status id is required.");

        RuleFor(x => x.ResolutionSummary)
            .MaximumLength(500)
            .When(x => x.ResolutionSummary is not null)
            .WithMessage("Resolution summary cannot exceed 500 characters.");
    }
}
