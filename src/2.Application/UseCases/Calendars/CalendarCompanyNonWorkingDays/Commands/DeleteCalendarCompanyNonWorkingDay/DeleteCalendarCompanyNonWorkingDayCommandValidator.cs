using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Commands.DeleteCalendarCompanyNonWorkingDay;

/// <summary>
/// Validates <see cref="DeleteCalendarCompanyNonWorkingDayCommand"/>.
/// </summary>
public sealed class DeleteCalendarCompanyNonWorkingDayCommandValidator : AbstractValidator<DeleteCalendarCompanyNonWorkingDayCommand>
{
    public DeleteCalendarCompanyNonWorkingDayCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("The holiday id is required.");
    }
}
