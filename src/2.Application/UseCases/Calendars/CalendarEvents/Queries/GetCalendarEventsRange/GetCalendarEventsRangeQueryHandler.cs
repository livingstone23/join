using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEvents.Queries.GetCalendarEventsRange;

/// <summary>
/// Handles <see cref="GetCalendarEventsRangeQuery"/> through <see cref="CalendarEventQueryService"/>.
/// </summary>
public sealed class GetCalendarEventsRangeQueryHandler(CalendarEventQueryService queryService)
    : IRequestHandler<GetCalendarEventsRangeQuery, Response<CalendarEventRangeDto>>
{
    public Task<Response<CalendarEventRangeDto>> Handle(GetCalendarEventsRangeQuery request, CancellationToken cancellationToken)
    {
        var opened = queryService.Open();
        if (!opened.IsSuccess)
        {
            return Task.FromResult(Response<CalendarEventRangeDto>.Error(opened.Message, opened.Errors));
        }

        var filter = new CalendarOccurrenceFilter(request.CalendarIds, request.ActivityCompanyIds, request.StatusCompanyIds, request.IncludeCancelled);
        return queryService.GetRangeAsync([opened.Data.UserId], request.From.UtcDateTime, request.To.UtcDateTime, filter, cancellationToken);
    }
}
