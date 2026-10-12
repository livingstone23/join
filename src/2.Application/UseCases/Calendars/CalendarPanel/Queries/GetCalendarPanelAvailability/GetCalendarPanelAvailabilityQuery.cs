using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelAvailability;

/// <summary>
/// Free slots of a user for an activity type in a range (at most 14 days), according to R4 and R5.
/// </summary>
public record GetCalendarPanelAvailabilityQuery(Guid UserId, DateTimeOffset From, DateTimeOffset To, Guid ActivityCompanyId)
    : IRequest<Response<CalendarAvailabilityDto>>;
