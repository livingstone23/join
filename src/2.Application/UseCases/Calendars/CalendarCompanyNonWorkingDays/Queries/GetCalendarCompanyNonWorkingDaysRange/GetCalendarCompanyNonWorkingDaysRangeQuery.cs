using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Queries.GetCalendarCompanyNonWorkingDaysRange;

/// <summary>
/// Holidays of a date range (both ends inclusive), for the views to mark them.
/// </summary>
public record GetCalendarCompanyNonWorkingDaysRangeQuery(DateOnly From, DateOnly To) : IRequest<Response<IReadOnlyList<CalendarCompanyNonWorkingDayDto>>>;
