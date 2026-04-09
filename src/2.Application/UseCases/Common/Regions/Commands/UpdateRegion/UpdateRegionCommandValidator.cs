using FluentValidation;

namespace JOIN.Application.UseCases.Common.Regions.Commands;

/// <summary>
/// Defines validation rules for <see cref="UpdateRegionCommand"/>.
/// </summary>
public sealed class UpdateRegionCommandValidator : AbstractValidator<UpdateRegionCommand>
{
    public UpdateRegionCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Region id is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Region name is required.")
            .MaximumLength(100).WithMessage("Region name cannot exceed 100 characters.");

        RuleFor(x => x.Code)
            .MaximumLength(20).WithMessage("Region code cannot exceed 20 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Code));

        RuleFor(x => x.CountryId)
            .NotEmpty().WithMessage("CountryId is required.");
    }
}
