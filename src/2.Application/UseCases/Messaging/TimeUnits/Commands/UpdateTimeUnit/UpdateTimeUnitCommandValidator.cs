using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TimeUnits.Commands;

/// <summary>
/// Defines validation rules for <see cref="UpdateTimeUnitCommand"/>.
/// </summary>
public sealed class UpdateTimeUnitCommandValidator : AbstractValidator<UpdateTimeUnitCommand>
{
    public UpdateTimeUnitCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Time unit id is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Time unit name is required.")
            .MaximumLength(50).WithMessage("Time unit name cannot exceed 50 characters.");

        RuleFor(x => x.Code)
            .GreaterThan(0).WithMessage("Time unit code must be greater than zero.");
    }
}
