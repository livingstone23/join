using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelEventLogs;

/// <summary>
/// Handles <see cref="GetCalendarPanelEventLogsQuery"/> through <see cref="CalendarEventQueryService"/>.
/// </summary>
public sealed class GetCalendarPanelEventLogsQueryHandler(CalendarEventQueryService queryService)
    : IRequestHandler<GetCalendarPanelEventLogsQuery, Response<IReadOnlyList<CalendarEventLogDto>>>
{
    public Task<Response<IReadOnlyList<CalendarEventLogDto>>> Handle(GetCalendarPanelEventLogsQuery request, CancellationToken cancellationToken) =>
        queryService.GetLogsAsync(request.Id, ownOnly: false, cancellationToken);
}
