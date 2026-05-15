using FluentValidation;

namespace JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Commands;

/// <summary>
/// Validation rules for <see cref="CreatePersonBusinessProfileCommand"/>.
/// </summary>
public sealed class CreatePersonBusinessProfileValidator : AbstractValidator<CreatePersonBusinessProfileCommand>
{
    /// <summary>
    /// Initializes validator rules for creating person business profile records.
    /// </summary>
    public CreatePersonBusinessProfileValidator()
    {
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
