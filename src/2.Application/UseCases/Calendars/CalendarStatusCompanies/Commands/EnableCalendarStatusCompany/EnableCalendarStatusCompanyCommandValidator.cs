using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.EnableCalendarStatusCompany;

/// <summary>
/// Validates <see cref="EnableCalendarStatusCompanyCommand"/>.
/// </summary>
public sealed class EnableCalendarStatusCompanyCommandValidator : AbstractValidator<EnableCalendarStatusCompanyCommand>
{
    public EnableCalendarStatusCompanyCommandValidator()
    {
        RuleFor(x => x.CalendarStatusId).NotEmpty().WithMessage("The global status is required.");
    }
}
