using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TicketStatusTransitions.Commands.RestoreTicketStatusTransition;

/// <summary>
/// Validates <see cref="RestoreTicketStatusTransitionCommand"/>.
/// </summary>
public sealed class RestoreTicketStatusTransitionCommandValidator : AbstractValidator<RestoreTicketStatusTransitionCommand>
{
    public RestoreTicketStatusTransitionCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
