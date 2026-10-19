using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Queries;

/// <summary>
/// SPEC 69 — returns the comments of an activity, most recent first, with the author full name and the
/// documents attached to each comment. Deleted comments stay in the list with <c>Body = null</c> and the soft
/// delete metadata filled in (rule 4).
/// </summary>
public sealed record ListCalendarEventCommentsQuery(Guid CalendarEventId)
    : IRequest<Response<IReadOnlyList<CalendarEventCommentDto>>>;