using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Commands;

/// <summary>
/// Command para actualizar la configuración de adjuntos de una empresa.
/// El <see cref="Id"/> viene del path del endpoint.
/// </summary>
public record UpdateTicketAttachmentSettingsCommand : ITransactionalCommand<Response<TicketAttachmentSettingsDto>>
{
    /// <summary>
    /// Identificador de la configuración a actualizar.
    /// </summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    /// <summary>
    /// Bitmask de tipos de documento permitidos.
    /// </summary>
    public int AllowedDocumentTypes { get; init; }

    /// <summary>
    /// Tamaño máximo permitido por archivo, en bytes.
    /// </summary>
    public long MaxFileSizeBytes { get; init; }

    /// <summary>
    /// Máximo de adjuntos activos permitidos por ticket.
    /// </summary>
    public int MaxFilesPerTicket { get; init; }

    /// <summary>
    /// Máximo de adjuntos por empresa por día UTC. <c>null</c> = sin límite.
    /// </summary>
    public int? MaxFilesPerDay { get; init; }
}