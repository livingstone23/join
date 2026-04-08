using FluentValidation;

namespace JOIN.Application.UseCases.Admin.IdentificationTypes.Commands;

/// <summary>
/// Defines validation rules for <see cref="DeleteIdentificationTypeCommand"/>.
/// </summary>
public sealed class DeleteIdentificationTypeCommandValidator : AbstractValidator<DeleteIdentificationTypeCommand>
{
    public DeleteIdentificationTypeCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Identification type id is required.");
    }
}
