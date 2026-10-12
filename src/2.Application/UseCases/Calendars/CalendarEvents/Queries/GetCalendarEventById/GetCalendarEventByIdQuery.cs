using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEvents.Queries.GetCalendarEventById;

/// <summary>
/// Detail of an activity of the current user (another user's activity answers 404).
/// </summary>
public record GetCalendarEventByIdQuery(Guid Id) : IRequest<Response<CalendarEventDto>>;
