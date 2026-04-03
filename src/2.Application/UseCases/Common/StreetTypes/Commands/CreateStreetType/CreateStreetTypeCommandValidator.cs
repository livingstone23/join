using FluentValidation;

namespace JOIN.Application.UseCases.Common.StreetTypes.Commands;

/// <summary>
/// Defines validation rules for <see cref="CreateStreetTypeCommand"/>.
/// </summary>
public class CreateStreetTypeCommandValidator : AbstractValidator<CreateStreetTypeCommand>
{
    public CreateStreetTypeCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Street type name is required.")
            .MaximumLength(50).WithMessage("Street type name cannot exceed 50 characters.");

        RuleFor(x => x.Abbreviation)
            .NotEmpty().WithMessage("Street type abbreviation is required.")
            .MaximumLength(10).WithMessage("Street type abbreviation cannot exceed 10 characters.");
    }
}
