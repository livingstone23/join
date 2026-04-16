using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands;

/// <summary>
/// Defines validation rules for ticket creation.
/// </summary>
public sealed class CreateTicketCommandValidator : AbstractValidator<CreateTicketCommand>
{
    public CreateTicketCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Ticket name is required.")
            .MaximumLength(150).WithMessage("Ticket name cannot exceed 150 characters.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Ticket description is required.")
            .MaximumLength(2000).WithMessage("Ticket description cannot exceed 2000 characters.");

        RuleFor(x => x.EstimatedTime)
            .GreaterThanOrEqualTo(0).WithMessage("Estimated time must be greater than or equal to zero.");

        RuleFor(x => x.ConsumedTime)
            .GreaterThanOrEqualTo(0).WithMessage("Consumed time must be greater than or equal to zero.");

        RuleFor(x => x.EffortPoints)
            .GreaterThanOrEqualTo(0)
            .When(x => x.EffortPoints.HasValue)
            .WithMessage("El puntaje de esfuerzo no puede ser negativo.");

        RuleFor(x => x.TicketStatusId)
            .NotEqual(Guid.Empty).WithMessage("Ticket status is required.");

        RuleFor(x => x.TicketComplexityId)
            .NotEqual(Guid.Empty).WithMessage("Ticket complexity is required.");

        RuleFor(x => x.TimeUnitId)
            .NotEqual(Guid.Empty).WithMessage("Time unit is required.");

        RuleFor(x => x.ChannelId)
            .NotEqual(Guid.Empty).WithMessage("Channel is required.");

        RuleFor(x => x.ConsumedTime)
            .LessThanOrEqualTo(x => x.EstimatedTime)
            .WithMessage("Consumed time cannot exceed estimated time.");

        RuleFor(x => x.AssignedToUserId)
            .Must(value => !value.HasValue || value.Value != Guid.Empty)
            .WithMessage("Assigned user id is invalid.");

        RuleFor(x => x.CustomerId)
            .Must(value => !value.HasValue || value.Value != Guid.Empty)
            .WithMessage("Customer id is invalid.");

        RuleFor(x => x.ProjectId)
            .Must(value => !value.HasValue || value.Value != Guid.Empty)
            .WithMessage("Project id is invalid.");

        RuleFor(x => x.AreaId)
            .Must(value => !value.HasValue || value.Value != Guid.Empty)
            .WithMessage("Area id is invalid.");

        RuleFor(x => x.PrecedentTicketId)
            .Must(value => !value.HasValue || value.Value != Guid.Empty)
            .WithMessage("Precedent ticket id is invalid.");
    }
}
