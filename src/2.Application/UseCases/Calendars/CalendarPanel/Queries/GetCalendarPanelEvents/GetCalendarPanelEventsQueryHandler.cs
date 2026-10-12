using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelEvents;

/// <summary>
/// Handles <see cref="GetCalendarPanelEventsQuery"/> through <see cref="CalendarEventQueryService"/>.
/// </summary>
public sealed class GetCalendarPanelEventsQueryHandler(CalendarEventQueryService queryService)
    : IRequestHandler<GetCalendarPanelEventsQuery, Response<CalendarEventRangeDto>>
{
    public Task<Response<CalendarEventRangeDto>> Handle(GetCalendarPanelEventsQuery request, CancellationToken cancellationToken)
    {
        var userIds = request.UserIds.Distinct().ToArray();
        if (userIds.Length == 0 || userIds.Length > CalendarQueryReader.MaxPanelUsers)
        {
            return Task.FromResult(Response<CalendarEventRangeDto>.Error(CalendarErrorCodes.TooManyUsers, [$"Select between 1 and {CalendarQueryReader.MaxPanelUsers} users."]));
        }

        var filter = new CalendarOccurrenceFilter(request.CalendarIds, request.ActivityCompanyIds, request.StatusCompanyIds, request.IncludeCancelled);
        return queryService.GetRangeAsync(userIds, request.From.UtcDateTime, request.To.UtcDateTime, filter, cancellationToken);
    }
}
