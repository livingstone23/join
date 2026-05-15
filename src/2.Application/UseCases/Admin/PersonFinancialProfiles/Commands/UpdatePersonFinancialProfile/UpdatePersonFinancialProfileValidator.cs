using FluentValidation;

namespace JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Commands;

/// <summary>
/// Validation rules for <see cref="UpdatePersonFinancialProfileCommand"/>.
/// </summary>
public sealed class UpdatePersonFinancialProfileValidator : AbstractValidator<UpdatePersonFinancialProfileCommand>
{
    /// <summary>
    /// Initializes a new instance of the validator with all required rules.
    /// </summary>
    public UpdatePersonFinancialProfileValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Financial profile id is required.");

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
