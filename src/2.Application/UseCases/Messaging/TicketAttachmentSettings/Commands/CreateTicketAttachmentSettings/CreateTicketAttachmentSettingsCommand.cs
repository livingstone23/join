using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Commands;

/// <summary>
/// Command para crear la configuración de adjuntos de tickets de una empresa.
/// </summary>
public record CreateTicketAttachmentSettingsCommand : ITransactionalCommand<Response<TicketAttachmentSettingsDto>>
{
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