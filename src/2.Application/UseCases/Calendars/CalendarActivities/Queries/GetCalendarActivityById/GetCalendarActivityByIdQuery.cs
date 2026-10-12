using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivities.Queries.GetCalendarActivityById;

/// <summary>
/// One global activity type.
/// </summary>
public record GetCalendarActivityByIdQuery(Guid Id) : IRequest<Response<CalendarActivityDto>>;
