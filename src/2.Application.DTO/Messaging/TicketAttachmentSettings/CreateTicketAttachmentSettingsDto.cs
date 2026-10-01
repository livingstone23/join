namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Payload de entrada para crear la configuración de adjuntos de tickets
/// de una empresa. <see cref="AllowedDocumentTypes"/> se envía como <c>int</c>
/// (el bitmask crudo de <c>JOIN.Domain.Enums.DocumentType</c>) — el DTO
/// no referencia tipos del Domain layer.
/// </summary>
public record CreateTicketAttachmentSettingsDto
{
    /// <summary>
    /// Bitmask de tipos permitidos. <c>0</c> deshabilita toda subida
    /// (todo chequeo HasFlag falla por construcción).
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