using FluentValidation;

namespace JOIN.Application.UseCases.Admin.Genders.Commands;

/// <summary>
/// Validates <see cref="RestoreGenderCommand"/>.
/// </summary>
public sealed class RestoreGenderCommandValidator : AbstractValidator<RestoreGenderCommand>
{
    public RestoreGenderCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
