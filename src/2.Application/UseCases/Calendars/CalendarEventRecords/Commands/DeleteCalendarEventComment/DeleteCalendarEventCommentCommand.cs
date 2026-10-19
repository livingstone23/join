using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Commands;

/// <summary>
/// SPEC 69 — soft-deletes a comment on a calendar activity (rule 4). The body and its documents stop being
/// returned by the API; the row stays for the bitácora with the reason in <c>Notes</c>. Deleting a comment does
/// not delete its documents (rule 11).
/// </summary>
public sealed record DeleteCalendarEventCommentCommand(Guid CalendarEventId, Guid CommentId, string Reason)
    : ITransactionalCommand<Response<bool>>;