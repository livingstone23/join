using FluentValidation;

namespace JOIN.Application.UseCases.Common.StreetTypes.Commands;

/// <summary>
/// Defines validation rules for <see cref="DeleteStreetTypeCommand"/>.
/// </summary>
public class DeleteStreetTypeCommandValidator : AbstractValidator<DeleteStreetTypeCommand>
{
    public DeleteStreetTypeCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Street type id is required.");
    }
}
