using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Queries;

/// <summary>
/// SPEC 69 — <c>GET /api/v1/CalendarEvents/{eventId}/documents/{id}/download</c>. Returns the bytes of a
/// previously uploaded <see cref="Domain.Calendars.CalendarEventDocument"/> from the storage provider that
/// stored it, gated by the activity's visibility (regla 5) and the <c>CalendarActivityRecords.CanDownload</c>
/// permission.
/// </summary>
public sealed record DownloadCalendarEventDocumentQuery(Guid EventId, Guid DocumentId)
    : IRequest<Response<CalendarEventDocumentDownloadResult>>;

/// <summary>
/// Stream and metadata of a downloaded calendar document. The controller turns it into a
/// <see cref="Microsoft.AspNetCore.Mvc.FileStreamResult"/>.
/// </summary>
public sealed record CalendarEventDocumentDownloadResult(
    Stream Content,
    string ContentType,
    string OriginalName);