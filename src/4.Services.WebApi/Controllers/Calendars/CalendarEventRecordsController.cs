using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.CalendarEventRecords.Commands;
using JOIN.Application.UseCases.Calendars.CalendarEventRecords.Queries;
using JOIN.Domain.Security;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Calendars;

/// <summary>
/// SPEC 69 — comments and documents of a calendar activity (regla 5 visibility; <c>CanCreate</c> to add,
/// <c>CanDownload</c> to download, <c>CanDelete</c> to soft-delete). The route is nested under the activity:
/// <c>GET/POST /api/v1/CalendarEvents/{eventId}/comments</c> and
/// <c>GET/POST /api/v1/CalendarEvents/{eventId}/documents</c>. The records menu option is hidden (the
/// controls live inside the activity detail tab); the resource is the only thing that matters for the
/// permission gate.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/CalendarEvents/{eventId:guid}")]
[Produces("application/json")]
[PermissionResource("CalendarActivityRecords")]
public class CalendarEventRecordsController(ISender sender) : ControllerBase
{
    /// <summary> Comments of the activity, most recent first. </summary>
    [HttpGet("comments")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<CalendarEventCommentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListComments(Guid eventId, CancellationToken cancellationToken) =>
        this.ToResult(await sender.Send(new ListCalendarEventCommentsQuery(eventId), cancellationToken));

    /// <summary> Adds a comment to the activity. Requires <c>CalendarActivityRecords.CanCreate</c>. </summary>
    [HttpPost("comments")]
    [ProducesResponseType(typeof(Response<CalendarEventCommentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateComment(Guid eventId, [FromBody] CreateCalendarEventCommentBody body, CancellationToken cancellationToken) =>
        this.ToResult(await sender.Send(new CreateCalendarEventCommentCommand(eventId, body.Body), cancellationToken));

    /// <summary> Soft-deletes a comment. Requires <c>CalendarActivityRecords.CanDelete</c>. </summary>
    [HttpDelete("comments/{commentId:guid}")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteComment(Guid eventId, Guid commentId, [FromBody] DeleteCommentBody body, CancellationToken cancellationToken) =>
        this.ToResult(await sender.Send(new DeleteCalendarEventCommentCommand(commentId, body.Reason), cancellationToken));

    /// <summary> Documents of the activity; pass <c>commentId</c> to filter by one of its comments. </summary>
    [HttpGet("documents")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<CalendarEventDocumentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDocuments(Guid eventId, [FromQuery] Guid? commentId, CancellationToken cancellationToken) =>
        this.ToResult(await sender.Send(new ListCalendarEventDocumentsQuery(eventId, commentId), cancellationToken));

    /// <summary> Uploads a document to the activity, optionally attached to one of its comments.
    /// Requires <c>CalendarActivityRecords.CanCreate</c>. </summary>
    [HttpPost("documents")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(Response<CalendarEventDocumentDto>), StatusCodes.Status200OK)]
    [RequestSizeLimit(200L * 1024 * 1024)] // 200 MB ceiling: the company limit is what matters.
    public async Task<IActionResult> UploadDocument(
        Guid eventId,
        [FromForm] UploadDocumentForm form,
        CancellationToken cancellationToken)
    {
        if (form.File is null || form.File.Length == 0)
        {
            return this.ToResult(Response<CalendarEventDocumentDto>.Error(
                "CALENDAR_DOCUMENT_CONTENT_EMPTY",
                ["The uploaded file is required."]));
        }

        await using var ms = new MemoryStream();
        await form.File.CopyToAsync(ms, cancellationToken);
        var command = new UploadCalendarEventDocumentCommand(
            eventId,
            form.CommentId,
            form.File.FileName,
            form.File.ContentType ?? "application/octet-stream",
            ms.ToArray());
        return this.ToResult(await sender.Send(command, cancellationToken));
    }

    /// <summary> Soft-deletes a document. Requires <c>CalendarActivityRecords.CanDelete</c>. </summary>
    [HttpDelete("documents/{documentId:guid}")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteDocument(Guid eventId, Guid documentId, [FromBody] DeleteCommentBody body, CancellationToken cancellationToken) =>
        this.ToResult(await sender.Send(new DeleteCalendarEventDocumentCommand(documentId, body.Reason), cancellationToken));

    /// <summary> Streams the file from the provider that stored it. Requires
    /// <c>CalendarActivityRecords.CanDownload</c> (plan §10). The activity visibility (regla 5) is enforced by
    /// the handler, so a foreign-tenant or hidden-activity probe resolves the same <c>CALENDAR_DOCUMENT_NOT_FOUND</c>
    /// code. </summary>
    [HttpGet("documents/{documentId:guid}/download")]
    [RequirePermission(PermissionFlags.CanDownload)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DownloadDocument(Guid eventId, Guid documentId, CancellationToken cancellationToken)
    {
        var response = await sender.Send(
            new DownloadCalendarEventDocumentQuery(eventId, documentId), cancellationToken);
        return response.IsSuccess
            ? File(response.Data!.Content, response.Data.ContentType, response.Data.OriginalName)
            : this.ToResult(response);
    }
}

/// <summary> Body for create-comment: <c>{ body }</c>. </summary>
public sealed record CreateCalendarEventCommentBody(string Body);

/// <summary> Body for delete-comment / delete-document: <c>{ reason }</c>. </summary>
public sealed record DeleteCommentBody(string Reason);

/// <summary> Multipart upload: file plus optional commentId. </summary>
public sealed class UploadDocumentForm
{
    public IFormFile? File { get; init; }
    public Guid? CommentId { get; init; }
}