using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Commands;

/// <summary>
/// SPEC 69 — adds a comment to a calendar activity. The route is nested under the activity (rule 5), so the
/// activity id travels in the URL; the body is the comment content. The visibility check uses the SPEC 68
/// service so a foreign-tenant probe answers the same code as a hidden activity.
/// </summary>
public sealed record CreateCalendarEventCommentCommand(Guid CalendarEventId, string Body)
    : ITransactionalCommand<Response<CalendarEventCommentDto>>;