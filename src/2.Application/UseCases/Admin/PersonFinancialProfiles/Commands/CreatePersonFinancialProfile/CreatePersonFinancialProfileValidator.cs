using FluentValidation;

namespace JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Commands;

/// <summary>
/// Validation rules for <see cref="CreatePersonFinancialProfileCommand"/>.
/// </summary>
public sealed class CreatePersonFinancialProfileValidator : AbstractValidator<CreatePersonFinancialProfileCommand>
{
    /// <summary>
    /// Initializes validator rules for creating person financial profile records.
    /// </summary>
    public CreatePersonFinancialProfileValidator()
    {
        RuleFor(x => x.PersonId)
            .NotEmpty().WithMessage("Person id is required.");

        RuleFor(x => x.IncomeRangeId)
            .NotEmpty().WithMessage("Income range id is required.");

        RuleFor(x => x.SourceOfFunds)
            .NotEmpty().WithMessage("Source of funds is required.")
            .MaximumLength(250).WithMessage("Source of funds cannot exceed 250 characters.");

        RuleFor(x => x.DeclaredDate)
            .NotEmpty().WithMessage("Declared date is required.");
    }
}
