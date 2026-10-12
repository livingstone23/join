using FluentValidation;
using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.UpdateCalendarActivityCompany;

/// <summary>
/// Validates <see cref="UpdateCalendarActivityCompanyCommand"/>.
/// </summary>
public sealed class UpdateCalendarActivityCompanyCommandValidator : AbstractValidator<UpdateCalendarActivityCompanyCommand>
{
    public UpdateCalendarActivityCompanyCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("The activity type id is required.");
        RuleFor(x => x.Name).ValidName(100);
        RuleFor(x => x.Color).ValidColor();
        RuleFor(x => x.DefaultDurationMinutes).ValidDuration();
        RuleFor(x => x.InitialCalendarStatusCompanyId).NotEmpty().WithMessage("The initial status is required.");
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0).WithMessage("The sort order cannot be negative.");
    }
}
