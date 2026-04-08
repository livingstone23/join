using FluentValidation;

namespace JOIN.Application.UseCases.Common.Municipalities.Commands;

/// <summary>
/// Defines validation rules for <see cref="CreateMunicipalityCommand"/>.
/// </summary>
public sealed class CreateMunicipalityCommandValidator : AbstractValidator<CreateMunicipalityCommand>
{
    public CreateMunicipalityCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Municipality name is required.")
            .MaximumLength(100).WithMessage("Municipality name cannot exceed 100 characters.");

        RuleFor(x => x.Code)
            .MaximumLength(20).WithMessage("Municipality code cannot exceed 20 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Code));

        RuleFor(x => x.ProvinceId)
            .NotEmpty().WithMessage("ProvinceId is required.");
    }
}
