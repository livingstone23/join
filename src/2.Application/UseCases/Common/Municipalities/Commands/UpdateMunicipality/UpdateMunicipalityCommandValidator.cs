using FluentValidation;

namespace JOIN.Application.UseCases.Common.Municipalities.Commands;

/// <summary>
/// Defines validation rules for <see cref="UpdateMunicipalityCommand"/>.
/// </summary>
public sealed class UpdateMunicipalityCommandValidator : AbstractValidator<UpdateMunicipalityCommand>
{
    public UpdateMunicipalityCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Municipality id is required.");

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
