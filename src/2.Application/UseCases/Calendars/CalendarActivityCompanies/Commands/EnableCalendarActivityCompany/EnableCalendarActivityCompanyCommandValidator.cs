using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.EnableCalendarActivityCompany;

/// <summary>
/// Validates <see cref="EnableCalendarActivityCompanyCommand"/>.
/// </summary>
public sealed class EnableCalendarActivityCompanyCommandValidator : AbstractValidator<EnableCalendarActivityCompanyCommand>
{
    public EnableCalendarActivityCompanyCommandValidator()
    {
        RuleFor(x => x.CalendarActivityId).NotEmpty().WithMessage("The global activity type is required.");
    }
}
