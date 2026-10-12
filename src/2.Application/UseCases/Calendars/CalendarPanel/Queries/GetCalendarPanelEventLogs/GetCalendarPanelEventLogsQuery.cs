using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelEventLogs;

/// <summary>
/// Log of any activity of the company.
/// </summary>
public record GetCalendarPanelEventLogsQuery(Guid Id) : IRequest<Response<IReadOnlyList<CalendarEventLogDto>>>;
