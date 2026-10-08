using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TicketStatuses.Commands;

/// <summary>
/// Validates <see cref="RestoreTicketStatusCommand"/>.
/// </summary>
public sealed class RestoreTicketStatusCommandValidator : AbstractValidator<RestoreTicketStatusCommand>
{
    public RestoreTicketStatusCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
