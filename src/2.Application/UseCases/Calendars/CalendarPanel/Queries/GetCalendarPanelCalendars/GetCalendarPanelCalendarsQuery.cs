using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelCalendars;

/// <summary>
/// Calendars of the selected users (at most 20).
/// </summary>
public record GetCalendarPanelCalendarsQuery(IReadOnlyCollection<Guid> UserIds) : IRequest<Response<IReadOnlyList<UserCalendarDto>>>;
