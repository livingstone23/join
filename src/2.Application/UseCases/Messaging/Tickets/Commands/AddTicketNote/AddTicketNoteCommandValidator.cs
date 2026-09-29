using FluentValidation;
using JOIN.Domain.Enums;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands.AddTicketNote;

/// <summary>
/// Defines validation rules for <see cref="AddTicketNoteCommand"/>.
/// Only <see cref="LogType.InternalNote"/> and <see cref="LogType.ExternalNote"/> are accepted;
/// the other log types are produced by the system, not by user input.
/// </summary>
public sealed class AddTicketNoteCommandValidator : AbstractValidator<AddTicketNoteCommand>
{
    public AddTicketNoteCommandValidator()
    {
        RuleFor(x => x.TicketId)
            .NotEqual(Guid.Empty)
            .WithMessage("Ticket id is required.");

        RuleFor(x => x.Summary)
            .NotEmpty().WithMessage("Note summary must be 1..1000 characters.")
            .MaximumLength(1000).WithMessage("Note summary must be 1..1000 characters.");

        RuleFor(x => x.LogType)
            .Must(t => t is LogType.InternalNote or LogType.ExternalNote)
            .WithMessage("LogType must be InternalNote or ExternalNote.");
    }
}
