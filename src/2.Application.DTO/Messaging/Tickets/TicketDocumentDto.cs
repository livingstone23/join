namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Payload de salida para un documento adjunto a un ticket. NO expone
/// <c>NewName</c>, <c>Path</c> ni <c>StorageProvider</c> — son detalle
/// interno de almacenamiento; el cliente descarga por <c>Id</c> a través
/// del endpoint dedicado, nunca construye la ruta.
/// </summary>
public sealed record TicketDocumentDto
{
    /// <summary>
    /// Identificador del documento.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Identificador del ticket al que pertenece.
    /// </summary>
    public Guid TicketId { get; init; }

    /// <summary>
    /// Identificador del log del ticket donde se subió el adjunto.
    /// </summary>
    public Guid TicketLogsId { get; init; }

    /// <summary>
    /// Tipo de documento como cadena legible (ej. "Pdf", "Image").
    /// </summary>
    public string DocumentType { get; init; } = string.Empty;

    /// <summary>
    /// Nombre original del archivo tal como lo subió el usuario.
    /// </summary>
    public string OriginalName { get; init; } = string.Empty;

    /// <summary>
    /// MIME type del archivo, conservado para servir la descarga.
    /// </summary>
    public string ContentType { get; init; } = string.Empty;

    /// <summary>
    /// Tamaño del archivo en bytes, observado al momento de la subida.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Identificador del usuario que subió el adjunto.
    /// </summary>
    public Guid? CreatedByUserId { get; init; }

    /// <summary>
    /// Nombre del usuario que subió el adjunto (proyectado vía JOIN).
    /// </summary>
    public string? CreatedByUserName { get; init; }

    /// <summary>
    /// Timestamp UTC de creación.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}