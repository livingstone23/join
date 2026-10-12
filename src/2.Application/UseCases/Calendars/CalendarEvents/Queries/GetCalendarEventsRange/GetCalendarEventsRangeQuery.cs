using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEvents.Queries.GetCalendarEventsRange;

/// <summary>
/// Main query of the views: expanded occurrences of the current user's calendars in a range (at most 62 days),
/// with holidays and the user's non-working periods.
/// </summary>
public record GetCalendarEventsRangeQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyCollection<Guid>? CalendarIds = null,
    IReadOnlyCollection<Guid>? ActivityCompanyIds = null,
    IReadOnlyCollection<Guid>? StatusCompanyIds = null,
    bool IncludeCancelled = false)
    : IRequest<Response<CalendarEventRangeDto>>;
