using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Queries.GetCalendarCompanyNonWorkingDayById;

/// <summary>
/// One company holiday.
/// </summary>
public record GetCalendarCompanyNonWorkingDayByIdQuery(Guid Id) : IRequest<Response<CalendarCompanyNonWorkingDayDto>>;
