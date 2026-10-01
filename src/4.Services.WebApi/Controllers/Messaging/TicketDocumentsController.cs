using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.DeleteTicketDocument;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.UploadTicketDocument;
using JOIN.Application.UseCases.Messaging.Tickets.Queries.DownloadTicketDocument;
using JOIN.Application.UseCases.Messaging.Tickets.Queries.GetTicketDocuments;
using JOIN.Domain.Security;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace JOIN.Services.WebApi.Controllers.Messaging;

/// <summary>
/// REST endpoints for ticket attachments. Esta es la primera ruta anidada
/// explícita del módulo — un documento no existe sin su ticket, así que la
/// URL refleja esa pertenencia: <c>api/v1/tickets/{ticketId}/documents</c>.
/// El recurso de permiso <c>TicketDocuments</c> es distinto de <c>Tickets</c>
/// para que una empresa pueda otorgar lectura de tickets sin habilitar
/// automáticamente la descarga de sus adjuntos.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/tickets/{ticketId:guid}/documents")]
[Produces("application/json")]
[PermissionResource("TicketDocuments")]
public class TicketDocumentsController(ISender sender) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    /// <summary>
    /// Lista los documentos adjuntos activos del ticket, ordenados por
    /// <c>Created DESC</c>.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<IReadOnlyCollection<TicketDocumentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetTicketDocumentsQuery(ticketId), cancellationToken);

        if (!response.IsSuccess)
        {
            return MapError(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Descarga el binario de un documento. <c>[RequirePermission(CanDownload)]</c>:
    /// el verbo HTTP (<c>GET</c>) implicaría <c>CanRead</c>, pero la acción
    /// semántica es descargar — <see cref="PermissionFlags.CanDownload"/>
    /// existía en el proyecto desde SPEC 12/16 sin ningún consumidor real.
    /// </summary>
    [HttpGet("{id:guid}/download")]
    [RequirePermission(PermissionFlags.CanDownload)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid ticketId, Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new DownloadTicketDocumentQuery(ticketId, id), cancellationToken);

        if (!response.IsSuccess)
        {
            return MapError(response);
        }

        // ASP.NET Core dispone el stream al terminar la respuesta.
        return File(response.Data!.Content, response.Data.ContentType, response.Data.OriginalName);
    }

    /// <summary>
    /// Sube un documento nuevo al ticket. <c>multipart/form-data</c> con el
    /// archivo en <c>file</c> y opcionalmente el <c>ticketLogsId</c> de
    /// anclaje (cuando se omite, el handler resuelve al log más reciente).
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(Response<TicketDocumentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Upload(
        Guid ticketId,
        [FromForm] IFormFile file,
        [FromForm] Guid? ticketLogsId,
        CancellationToken cancellationToken = default)
    {
        if (file is null)
        {
            return BadRequest(Response<object>.Error("INVALID_FILE", ["The 'file' form field is required."]));
        }

        var attachment = new InboundAttachment(
            file.OpenReadStream(),
            file.FileName,
            file.ContentType ?? "application/octet-stream",
            file.Length);

        var command = new UploadTicketDocumentCommand
        {
            TicketId = ticketId,
            TicketLogsId = ticketLogsId,
            File = attachment
        };

        var response = await _sender.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return MapError(response);
        }

        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>
    /// Borrado lógico del documento (soft delete de metadata; el binario
    /// permanece como evidencia del ciclo de vida del ticket).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid ticketId, Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new DeleteTicketDocumentCommand { TicketId = ticketId, Id = id }, cancellationToken);

        if (!response.IsSuccess)
        {
            return MapError(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Mapea códigos de negocio a HTTP, coherente con el resto del módulo
    /// (mismo patrón que <c>TicketAttachmentSettingsController.MapError</c>).
    /// </summary>
    private IActionResult MapError<T>(Response<T> response)
    {
        return response.Message switch
        {
            "COMPANY_REQUIRED" or "USER_REQUIRED"
                => Unauthorized(response),
            "TICKET_NOT_FOUND" or "TICKET_DOCUMENT_NOT_FOUND" or "FILE_NOT_FOUND_IN_STORAGE"
                => NotFound(response),
            "MAX_FILES_PER_TICKET_REACHED"
                => Conflict(response),
            "DAILY_ATTACHMENT_QUOTA_REACHED"
                => StatusCode(StatusCodes.Status429TooManyRequests, response),
            _ => BadRequest(response)
        };
    }
}