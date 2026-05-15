using FluentValidation;

namespace JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Commands;

/// <summary>
/// Validation rules for <see cref="UpdatePersonBusinessProfileCommand"/>.
/// </summary>
public sealed class UpdatePersonBusinessProfileValidator : AbstractValidator<UpdatePersonBusinessProfileCommand>
{
    /// <summary>
    /// Initializes a new instance of the validator with all required rules.
    /// </summary>
    public UpdatePersonBusinessProfileValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Business profile id is required.");

        RuleFor(x => x.PersonId)
            .NotEmpty().WithMessage("Person id is required.");

        RuleFor(x => x.IndustryId)
            .NotEmpty().WithMessage("Industry id is required.");

        RuleFor(x => x.TaxRegimeId)
            .NotEmpty().WithMessage("Tax regime id is required.");

        RuleFor(x => x.Website)
            .MaximumLength(255).WithMessage("Website cannot exceed 255 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Website));
    }
}
