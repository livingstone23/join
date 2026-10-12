using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEvents.Queries.GetCalendarEventLogs;

/// <summary>
/// Handles <see cref="GetCalendarEventLogsQuery"/> through <see cref="CalendarEventQueryService"/>.
/// </summary>
public sealed class GetCalendarEventLogsQueryHandler(CalendarEventQueryService queryService)
    : IRequestHandler<GetCalendarEventLogsQuery, Response<IReadOnlyList<CalendarEventLogDto>>>
{
    public Task<Response<IReadOnlyList<CalendarEventLogDto>>> Handle(GetCalendarEventLogsQuery request, CancellationToken cancellationToken) =>
        queryService.GetLogsAsync(request.Id, ownOnly: true, cancellationToken);
}
