using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivities.Queries.GetCalendarActivities;

/// <summary>
/// Paged global activity types, filtered by code/name (<c>search</c>) and active flag.
/// </summary>
public record GetCalendarActivitiesQuery(string? Search = null, bool? IsActive = null, int? PageNumber = null, int? PageSize = null)
    : IRequest<Response<PagedResult<CalendarActivityDto>>>;
