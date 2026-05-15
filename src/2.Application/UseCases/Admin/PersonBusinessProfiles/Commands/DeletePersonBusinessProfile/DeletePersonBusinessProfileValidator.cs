using FluentValidation;

namespace JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Commands;

/// <summary>
/// Validation rules for <see cref="DeletePersonBusinessProfileCommand"/>.
/// </summary>
public sealed class DeletePersonBusinessProfileValidator : AbstractValidator<DeletePersonBusinessProfileCommand>
{
    /// <summary>
    /// Initializes validator rules for deleting person business profile records.
    /// </summary>
    public DeletePersonBusinessProfileValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Business profile id is required.");
    }
}
