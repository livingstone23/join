using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelEventById;

/// <summary>
/// Detail of any activity of the company.
/// </summary>
public record GetCalendarPanelEventByIdQuery(Guid Id) : IRequest<Response<CalendarEventDto>>;
