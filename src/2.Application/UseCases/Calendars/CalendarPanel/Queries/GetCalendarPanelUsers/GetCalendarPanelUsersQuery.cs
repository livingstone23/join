using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelUsers;

/// <summary>
/// Users of the company for the panel filter (name, e-mail, <c>hasCalendar</c>).
/// </summary>
public record GetCalendarPanelUsersQuery(string? Search = null, bool? OnlyWithCalendar = null, int? PageNumber = null, int? PageSize = null)
    : IRequest<Response<PagedResult<CalendarPanelUserDto>>>;
