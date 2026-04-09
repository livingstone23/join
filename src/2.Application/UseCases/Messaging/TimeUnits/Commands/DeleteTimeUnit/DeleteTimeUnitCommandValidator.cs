using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TimeUnits.Commands;

/// <summary>
/// Defines validation rules for <see cref="DeleteTimeUnitCommand"/>.
/// </summary>
public sealed class DeleteTimeUnitCommandValidator : AbstractValidator<DeleteTimeUnitCommand>
{
    public DeleteTimeUnitCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Time unit id is required.");
    }
}
