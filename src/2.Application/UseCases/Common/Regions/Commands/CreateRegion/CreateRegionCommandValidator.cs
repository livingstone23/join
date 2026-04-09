using FluentValidation;

namespace JOIN.Application.UseCases.Common.Regions.Commands;

/// <summary>
/// Defines validation rules for <see cref="CreateRegionCommand"/>.
/// </summary>
public sealed class CreateRegionCommandValidator : AbstractValidator<CreateRegionCommand>
{
    public CreateRegionCommandValidator()
    {
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
