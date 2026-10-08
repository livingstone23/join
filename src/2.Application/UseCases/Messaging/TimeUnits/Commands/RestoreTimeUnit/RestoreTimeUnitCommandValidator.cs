using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TimeUnits.Commands;

/// <summary>
/// Validates <see cref="RestoreTimeUnitCommand"/>.
/// </summary>
public sealed class RestoreTimeUnitCommandValidator : AbstractValidator<RestoreTimeUnitCommand>
{
    public RestoreTimeUnitCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
