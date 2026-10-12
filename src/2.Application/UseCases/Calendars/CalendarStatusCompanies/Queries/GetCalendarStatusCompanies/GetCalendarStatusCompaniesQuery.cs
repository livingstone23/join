using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Queries.GetCalendarStatusCompanies;

/// <summary>
/// Paged statuses of the company with the global values.
/// </summary>
public record GetCalendarStatusCompaniesQuery(string? Search = null, bool? IsActive = null, int? PageNumber = null, int? PageSize = null)
    : IRequest<Response<PagedResult<CalendarStatusCompanyDto>>>;
