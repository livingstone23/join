using JOIN.Application.DTO.Calendars;
using JOIN.Domain.Calendars;

namespace JOIN.Application.Mappings;

/// <summary>
/// Mapperly operations of the calendar module (SPEC 44): entity → DTO for the write responses.
/// </summary>
public interface ICalendarMapper
{
    CalendarActivityDto ToDto(CalendarActivity entity);

    CalendarStatusDto ToDto(CalendarStatus entity);

    UserCalendarDto ToDto(UserCalendar entity);

    CalendarCompanyNonWorkingDayDto ToDto(CalendarCompanyNonWorkingDay entity);

    CalendarUserNonWorkingPeriodDto ToDto(CalendarUserNonWorkingPeriod entity);
}
