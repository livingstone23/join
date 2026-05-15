using FluentValidation;

namespace JOIN.Application.UseCases.Admin.Genders.Commands;

/// <summary>
/// Validates the payload used to delete a tenant-scoped gender.
/// </summary>
public sealed class DeleteGenderCommandValidator : AbstractValidator<DeleteGenderCommand>
{
    public DeleteGenderCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
