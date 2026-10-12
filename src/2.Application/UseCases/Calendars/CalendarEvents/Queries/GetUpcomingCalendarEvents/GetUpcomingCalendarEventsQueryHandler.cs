using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEvents.Queries.GetUpcomingCalendarEvents;

/// <summary>
/// Handles <see cref="GetUpcomingCalendarEventsQuery"/> through <see cref="CalendarEventQueryService"/>.
/// </summary>
public sealed class GetUpcomingCalendarEventsQueryHandler(CalendarEventQueryService queryService)
    : IRequestHandler<GetUpcomingCalendarEventsQuery, Response<CalendarEventRangeDto>>
{
    public Task<Response<CalendarEventRangeDto>> Handle(GetUpcomingCalendarEventsQuery request, CancellationToken cancellationToken)
    {
        if (request.Days is < 1 or > 31)
        {
            return Task.FromResult(Response<CalendarEventRangeDto>.Error(CalendarErrorCodes.RangeTooLarge, ["days must be between 1 and 31."]));
        }

        var opened = queryService.Open();
        if (!opened.IsSuccess)
        {
            return Task.FromResult(Response<CalendarEventRangeDto>.Error(opened.Message, opened.Errors));
        }

        var now = DateTime.UtcNow;
        return queryService.GetRangeAsync([opened.Data.UserId], now, now.AddDays(request.Days),
            new CalendarOccurrenceFilter(ExcludeFinal: true), cancellationToken);
    }
}
