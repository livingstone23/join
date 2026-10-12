using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelEvents;

/// <summary>
/// Like <c>GET /CalendarEvents</c> for several users (at most 20 users and 62 days). Other users' non-working
/// periods appear as <c>Unavailable</c>, without reason.
/// </summary>
public record GetCalendarPanelEventsQuery(
    IReadOnlyCollection<Guid> UserIds,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyCollection<Guid>? CalendarIds = null,
    IReadOnlyCollection<Guid>? ActivityCompanyIds = null,
    IReadOnlyCollection<Guid>? StatusCompanyIds = null,
    bool IncludeCancelled = false)
    : IRequest<Response<CalendarEventRangeDto>>;
