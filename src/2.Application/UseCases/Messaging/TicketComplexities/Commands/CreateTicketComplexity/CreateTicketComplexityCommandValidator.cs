using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TicketComplexities.Commands;

/// <summary>
/// Defines validation rules for <see cref="CreateTicketComplexityCommand"/>.
/// </summary>
public sealed class CreateTicketComplexityCommandValidator : AbstractValidator<CreateTicketComplexityCommand>
{
    public CreateTicketComplexityCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Ticket complexity name is required.")
            .MaximumLength(50).WithMessage("Ticket complexity name cannot exceed 50 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(200).WithMessage("Ticket complexity description cannot exceed 200 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Description));

        RuleFor(x => x.Code)
            .GreaterThan(0).WithMessage("Ticket complexity code must be greater than zero.");

        RuleFor(x => x.ResolutionTimeUnits)
            .GreaterThan(0).WithMessage("Resolution time units must be greater than zero.");

        RuleFor(x => x.TimeUnitId)
            .NotEmpty().WithMessage("Time unit id is required.");
    }
}
