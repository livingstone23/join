using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityIndex.Queries;

/// <summary>
/// SPEC 68 — log of one activity; same visibility rule as <see cref="GetCalendarActivityIndexEventByIdQuery"/>.
/// </summary>
public sealed record GetCalendarActivityIndexEventLogsQuery(Guid Id)
    : IRequest<Response<IReadOnlyList<CalendarEventLogDto>>>;