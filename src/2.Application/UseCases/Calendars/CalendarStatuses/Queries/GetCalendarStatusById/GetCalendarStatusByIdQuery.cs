using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarStatuses.Queries.GetCalendarStatusById;

/// <summary>
/// One global status.
/// </summary>
public record GetCalendarStatusByIdQuery(Guid Id) : IRequest<Response<CalendarStatusDto>>;
