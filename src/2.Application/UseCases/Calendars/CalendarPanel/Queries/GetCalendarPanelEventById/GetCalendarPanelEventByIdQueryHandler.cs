using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelEventById;

/// <summary>
/// Handles <see cref="GetCalendarPanelEventByIdQuery"/> through <see cref="CalendarEventQueryService"/>.
/// </summary>
public sealed class GetCalendarPanelEventByIdQueryHandler(CalendarEventQueryService queryService)
    : IRequestHandler<GetCalendarPanelEventByIdQuery, Response<CalendarEventDto>>
{
    public Task<Response<CalendarEventDto>> Handle(GetCalendarPanelEventByIdQuery request, CancellationToken cancellationToken) =>
        queryService.GetByIdAsync(request.Id, ownOnly: false, cancellationToken);
}
