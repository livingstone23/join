using FluentValidation;

namespace JOIN.Application.UseCases.Common.Regions.Commands;

/// <summary>
/// Validates <see cref="RestoreRegionCommand"/>.
/// </summary>
public sealed class RestoreRegionCommandValidator : AbstractValidator<RestoreRegionCommand>
{
    public RestoreRegionCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
