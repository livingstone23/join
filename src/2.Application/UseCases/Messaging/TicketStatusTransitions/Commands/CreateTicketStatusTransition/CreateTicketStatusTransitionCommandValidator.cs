using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TicketStatusTransitions.Commands.CreateTicketStatusTransition;

/// <summary>
/// Defines validation rules for <see cref="CreateTicketStatusTransitionCommand"/>.
/// The "from != to" rule is enforced here so the rejection is symmetric and predictable;
/// the handler still defends against it as a safety net for direct invocations that skip the pipeline.
/// </summary>
public sealed class CreateTicketStatusTransitionCommandValidator : AbstractValidator<CreateTicketStatusTransitionCommand>
{
    public CreateTicketStatusTransitionCommandValidator()
    {
        RuleFor(x => x.FromStatusId)
            .NotEqual(Guid.Empty)
            .WithMessage("FromStatusId is required.");

        RuleFor(x => x.ToStatusId)
            .NotEqual(Guid.Empty)
            .WithMessage("ToStatusId is required.");

        RuleFor(x => x)
            .Must(x => x.FromStatusId != x.ToStatusId)
            .WithMessage("SAME_STATUS_TRANSITION");
    }
}