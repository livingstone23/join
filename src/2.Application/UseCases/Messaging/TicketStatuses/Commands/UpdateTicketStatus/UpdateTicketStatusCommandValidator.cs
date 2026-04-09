using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TicketStatuses.Commands;

/// <summary>
/// Defines validation rules for <see cref="UpdateTicketStatusCommand"/>.
/// </summary>
public sealed class UpdateTicketStatusCommandValidator : AbstractValidator<UpdateTicketStatusCommand>
{
    public UpdateTicketStatusCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Ticket status id is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Ticket status name is required.")
            .MaximumLength(50).WithMessage("Ticket status name cannot exceed 50 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(200).WithMessage("Ticket status description cannot exceed 200 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Description));

        RuleFor(x => x.Code)
            .GreaterThan(0).WithMessage("Ticket status code must be greater than zero.");
    }
}
