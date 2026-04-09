using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TimeUnits.Commands;

/// <summary>
/// Defines validation rules for <see cref="CreateTimeUnitCommand"/>.
/// </summary>
public sealed class CreateTimeUnitCommandValidator : AbstractValidator<CreateTimeUnitCommand>
{
    public CreateTimeUnitCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Time unit name is required.")
            .MaximumLength(50).WithMessage("Time unit name cannot exceed 50 characters.");

        RuleFor(x => x.Code)
            .GreaterThan(0).WithMessage("Time unit code must be greater than zero.");
    }
}
