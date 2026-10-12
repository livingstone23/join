using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Queries.GetUsableCalendarActivityCompanies;

/// <summary>
/// Lookup for the activity form: only usable types (active in the company and globally), ordered.
/// </summary>
public record GetUsableCalendarActivityCompaniesQuery : IRequest<Response<IReadOnlyList<CalendarActivityCompanyDto>>>;
