using FluentValidation;

namespace JOIN.Application.UseCases.Common.Countries.Commands;

/// <summary>
/// Defines validation rules for <see cref="UpdateCountryCommand"/>.
/// </summary>
public class UpdateCountryCommandValidator : AbstractValidator<UpdateCountryCommand>
{
    public UpdateCountryCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Country id is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Country name is required.")
            .MaximumLength(100).WithMessage("Country name cannot exceed 100 characters.");

        RuleFor(x => x.IsoCode)
            .NotEmpty().WithMessage("ISO code is required.")
            .MaximumLength(10).WithMessage("ISO code cannot exceed 10 characters.");
    }
}
