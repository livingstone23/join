using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEvents.Queries.GetCalendarEventById;

/// <summary>
/// Handles <see cref="GetCalendarEventByIdQuery"/> through <see cref="CalendarEventQueryService"/>.
/// </summary>
public sealed class GetCalendarEventByIdQueryHandler(CalendarEventQueryService queryService)
    : IRequestHandler<GetCalendarEventByIdQuery, Response<CalendarEventDto>>
{
    public Task<Response<CalendarEventDto>> Handle(GetCalendarEventByIdQuery request, CancellationToken cancellationToken) =>
        queryService.GetByIdAsync(request.Id, ownOnly: true, cancellationToken);
}
