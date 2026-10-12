using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Queries.GetCalendarCompanyNonWorkingDays;

/// <summary>
/// Paged holidays of a year (current year by default), recurring ones resolved for that year.
/// </summary>
public record GetCalendarCompanyNonWorkingDaysQuery(int? Year = null, int? PageNumber = null, int? PageSize = null)
    : IRequest<Response<PagedResult<CalendarCompanyNonWorkingDayDto>>>;
