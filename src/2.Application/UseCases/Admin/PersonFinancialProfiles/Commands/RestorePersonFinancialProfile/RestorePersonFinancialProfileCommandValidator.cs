using FluentValidation;

namespace JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Commands;

/// <summary>
/// Validates <see cref="RestorePersonFinancialProfileCommand"/>.
/// </summary>
public sealed class RestorePersonFinancialProfileCommandValidator : AbstractValidator<RestorePersonFinancialProfileCommand>
{
    public RestorePersonFinancialProfileCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
