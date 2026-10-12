using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Commands.CreateCalendarCompanyNonWorkingDay;

/// <summary>
/// Registers a company holiday. A recurring holiday repeats every year on the same day and month.
/// </summary>
public record CreateCalendarCompanyNonWorkingDayCommand : ITransactionalCommand<Response<CalendarCompanyNonWorkingDayDto>>
{
    public DateOnly Date { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsRecurringYearly { get; init; }
}
