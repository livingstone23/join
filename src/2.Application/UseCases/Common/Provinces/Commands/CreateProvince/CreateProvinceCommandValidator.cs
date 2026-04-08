using FluentValidation;

namespace JOIN.Application.UseCases.Common.Provinces.Commands;

/// <summary>
/// Defines validation rules for <see cref="CreateProvinceCommand"/>.
/// </summary>
public class CreateProvinceCommandValidator : AbstractValidator<CreateProvinceCommand>
{
    public CreateProvinceCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Province name is required.")
            .MaximumLength(100).WithMessage("Province name cannot exceed 100 characters.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Province code is required.")
            .MaximumLength(20).WithMessage("Province code cannot exceed 20 characters.");

        RuleFor(x => x.CountryId)
            .NotEmpty().WithMessage("CountryId is required.");
    }
}