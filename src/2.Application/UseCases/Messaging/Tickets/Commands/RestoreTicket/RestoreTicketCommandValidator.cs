using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands;

/// <summary>
/// Validates <see cref="RestoreTicketCommand"/>.
/// </summary>
public sealed class RestoreTicketCommandValidator : AbstractValidator<RestoreTicketCommand>
{
    public RestoreTicketCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
