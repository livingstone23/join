using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanies.Queries.GetCurrentCalendarCompany;

/// <summary>
/// Calendar configuration of the current company, with <c>isConfigured</c>, <c>moduleEnabled</c> and the number of
/// users without calendar. Never "not found".
/// </summary>
public record GetCurrentCalendarCompanyQuery : IRequest<Response<CalendarCompanyDto>>;
