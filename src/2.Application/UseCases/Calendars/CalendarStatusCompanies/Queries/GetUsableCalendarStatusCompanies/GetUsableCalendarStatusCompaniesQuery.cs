using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Queries.GetUsableCalendarStatusCompanies;

/// <summary>
/// Lookup of usable statuses (active in the company and globally), ordered.
/// </summary>
public record GetUsableCalendarStatusCompaniesQuery : IRequest<Response<IReadOnlyList<CalendarStatusCompanyDto>>>;
