using FluentValidation;
using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Commands.CreateCalendarCompanyNonWorkingDay;

/// <summary>
/// Validates <see cref="CreateCalendarCompanyNonWorkingDayCommand"/>.
/// </summary>
public sealed class CreateCalendarCompanyNonWorkingDayCommandValidator : AbstractValidator<CreateCalendarCompanyNonWorkingDayCommand>
{
    public CreateCalendarCompanyNonWorkingDayCommandValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly)).WithMessage("The date is required.");
        RuleFor(x => x.Name).ValidName(150);
    }
}
