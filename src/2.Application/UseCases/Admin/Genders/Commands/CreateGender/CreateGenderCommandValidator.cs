using FluentValidation;

namespace JOIN.Application.UseCases.Admin.Genders.Commands;

/// <summary>
/// Validates the payload used to create a tenant-scoped gender.
/// </summary>
public sealed class CreateGenderCommandValidator : AbstractValidator<CreateGenderCommand>
{
    public CreateGenderCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(20)
            .WithMessage("Gender code is required and must not exceed 20 characters.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100)
            .WithMessage("Gender name is required and must not exceed 100 characters.");
    }
}
