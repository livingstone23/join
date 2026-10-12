using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Commands.DeleteCalendarCompanyNonWorkingDay;

/// <summary>
/// Logically deletes a company holiday.
/// </summary>
public record DeleteCalendarCompanyNonWorkingDayCommand(Guid Id) : ITransactionalCommand<Response<bool>>;
