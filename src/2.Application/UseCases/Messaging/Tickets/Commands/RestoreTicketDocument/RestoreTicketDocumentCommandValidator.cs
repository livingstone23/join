using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands.RestoreTicketDocument;

/// <summary>
/// Validates <see cref="RestoreTicketDocumentCommand"/>.
/// </summary>
public sealed class RestoreTicketDocumentCommandValidator : AbstractValidator<RestoreTicketDocumentCommand>
{
    public RestoreTicketDocumentCommandValidator()
    {
        RuleFor(x => x.TicketId)
            .NotEmpty()
            .WithMessage("TicketId is required.");

        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
