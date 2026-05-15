using FluentValidation;

namespace JOIN.Application.UseCases.Admin.PersonEmployments.Commands;

/// <summary>
/// Validation rules for <see cref="DeletePersonEmploymentCommand"/>.
/// </summary>
public sealed class DeletePersonEmploymentValidator : AbstractValidator<DeletePersonEmploymentCommand>
{
    /// <summary>
    /// Initializes validator rules for deleting person employment records.
    /// </summary>
    public DeletePersonEmploymentValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Employment id is required.");
    }
}
