using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Commands;

/// <summary>
/// SPEC 69 — soft-deletes a document attached to a calendar activity (rule 4). The row stays in the table but
/// the API no longer returns it for download (the file stays on disk).
/// </summary>
public sealed record DeleteCalendarEventDocumentCommand(Guid DocumentId, string Reason)
    : ITransactionalCommand<Response<bool>>;