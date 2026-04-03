using FluentValidation;

namespace JOIN.Application.UseCases.Common.StreetTypes.Commands;

/// <summary>
/// Defines validation rules for <see cref="UpdateStreetTypeCommand"/>.
/// </summary>
public class UpdateStreetTypeCommandValidator : AbstractValidator<UpdateStreetTypeCommand>
{
    public UpdateStreetTypeCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Street type id is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Street type name is required.")
            .MaximumLength(50).WithMessage("Street type name cannot exceed 50 characters.");

        RuleFor(x => x.Abbreviation)
            .NotEmpty().WithMessage("Street type abbreviation is required.")
            .MaximumLength(10).WithMessage("Street type abbreviation cannot exceed 10 characters.");
    }
}
