using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEvents.Queries.GetCalendarEventLogs;

/// <summary>
/// Log of an activity of the current user.
/// </summary>
public record GetCalendarEventLogsQuery(Guid Id) : IRequest<Response<IReadOnlyList<CalendarEventLogDto>>>;
