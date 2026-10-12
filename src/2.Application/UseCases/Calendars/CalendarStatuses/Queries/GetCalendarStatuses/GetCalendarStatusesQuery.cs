using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarStatuses.Queries.GetCalendarStatuses;

/// <summary>
/// Paged global statuses, filtered by code/name (<c>search</c>) and active flag.
/// </summary>
public record GetCalendarStatusesQuery(string? Search = null, bool? IsActive = null, int? PageNumber = null, int? PageSize = null)
    : IRequest<Response<PagedResult<CalendarStatusDto>>>;
