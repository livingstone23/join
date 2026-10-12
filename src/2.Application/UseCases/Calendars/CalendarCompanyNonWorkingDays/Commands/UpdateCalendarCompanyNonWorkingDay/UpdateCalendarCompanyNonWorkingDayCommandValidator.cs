using FluentValidation;
using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Commands.UpdateCalendarCompanyNonWorkingDay;

/// <summary>
/// Validates <see cref="UpdateCalendarCompanyNonWorkingDayCommand"/>.
/// </summary>
public sealed class UpdateCalendarCompanyNonWorkingDayCommandValidator : AbstractValidator<UpdateCalendarCompanyNonWorkingDayCommand>
{
    public UpdateCalendarCompanyNonWorkingDayCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("The holiday id is required.");
        RuleFor(x => x.Date).NotEqual(default(DateOnly)).WithMessage("The date is required.");
        RuleFor(x => x.Name).ValidName(150);
    }
}
