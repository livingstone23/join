namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Payload de entrada para actualizar la configuración de adjuntos de tickets
/// de una empresa. Misma forma que <see cref="CreateTicketAttachmentSettingsDto"/>
/// — el <c>Id</c> viene del path del endpoint.
/// </summary>
public record UpdateTicketAttachmentSettingsDto
{
    /// <summary>
    /// Bitmask de tipos permitidos.
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