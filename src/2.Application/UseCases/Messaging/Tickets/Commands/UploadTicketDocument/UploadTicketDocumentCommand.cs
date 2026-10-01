using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.UseCases.Messaging.Tickets;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands.UploadTicketDocument;

/// <summary>
/// Command para subir un documento adjunto a un ticket. Recibe el
/// <see cref="Stream"/> dentro de <see cref="InboundAttachment"/>, provisto
/// por el controller (<c>IFormFile.OpenReadStream()</c>), no un
/// <c>IFormFile</c> — el Application layer no referencia
/// <c>Microsoft.AspNetCore.Http</c>.
/// <see cref="TicketLogsId"/> es opcional; cuando es <c>null</c>, el
/// handler resuelve el log activo más reciente del ticket.
/// </summary>
public record UploadTicketDocumentCommand : ITransactionalCommand<Response<TicketDocumentDto>>
{
    /// <summary>
    /// Identificador del ticket al que se adjunta el archivo.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; init; }

    /// <summary>
    /// Identificador del log del ticket donde se ancla el adjunto
    /// (opcional — <c>null</c> resuelve al log más reciente activo).
    /// </summary>
    public Guid? TicketLogsId { get; init; }

    /// <summary>
    /// Archivo entrante (stream + metadata) agnóstico del transporte HTTP.
    /// </summary>
    public InboundAttachment File { get; init; } = null!;
}