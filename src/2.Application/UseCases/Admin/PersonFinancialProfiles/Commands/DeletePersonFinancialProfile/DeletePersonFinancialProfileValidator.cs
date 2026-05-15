using FluentValidation;

namespace JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Commands;

/// <summary>
/// Validation rules for <see cref="DeletePersonFinancialProfileCommand"/>.
/// </summary>
public sealed class DeletePersonFinancialProfileValidator : AbstractValidator<DeletePersonFinancialProfileCommand>
{
    /// <summary>
    /// Initializes validator rules for deleting person financial profile records.
    /// </summary>
    public DeletePersonFinancialProfileValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Financial profile id is required.");
    }
}
