using FluentValidation;

namespace JOIN.Application.UseCases.Admin.Genders.Commands;

/// <summary>
/// Validates the payload used to update a tenant-scoped gender.
/// </summary>
public sealed class UpdateGenderCommandValidator : AbstractValidator<UpdateGenderCommand>
{
    public UpdateGenderCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");

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
