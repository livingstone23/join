using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEvents.Queries.GetUpcomingCalendarEvents;

/// <summary>
/// Upcoming activities of the current user that are not cancelled nor completed (final statuses), for the welcome
/// screen. <c>days</c> between 1 and 31 (default 7).
/// </summary>
public record GetUpcomingCalendarEventsQuery(int Days = 7) : IRequest<Response<CalendarEventRangeDto>>;
