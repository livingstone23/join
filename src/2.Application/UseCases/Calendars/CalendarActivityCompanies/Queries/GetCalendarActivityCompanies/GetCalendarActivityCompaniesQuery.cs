using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Queries.GetCalendarActivityCompanies;

/// <summary>
/// Paged activity types of the company with effective and global values.
/// </summary>
public record GetCalendarActivityCompaniesQuery(string? Search = null, bool? IsActive = null, int? PageNumber = null, int? PageSize = null)
    : IRequest<Response<PagedResult<CalendarActivityCompanyDto>>>;
