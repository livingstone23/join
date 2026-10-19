using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Commands;

/// <summary>
/// SPEC 69 — uploads a document to a calendar activity, optionally tied to one of its comments (regla 2). The
/// controller reads the multipart file and passes the bytes plus the original name and content type; the
/// server infers the <c>DocumentType</c> from the extension.
/// </summary>
public sealed record UploadCalendarEventDocumentCommand(
    Guid CalendarEventId,
    Guid? CalendarEventCommentId,
    string OriginalName,
    string ContentType,
    byte[] Content)
    : ITransactionalCommand<Response<CalendarEventDocumentDto>>;