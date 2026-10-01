namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Payload de salida para la configuración de adjuntos de tickets de una
/// empresa. Una sola fila activa por tenant — el endpoint de listado
/// tenant-scoped devuelve 0 o 1 elemento.
/// </summary>
public record TicketAttachmentSettingsDto
{
    /// <summary>
    /// Identificador de la configuración.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Identificador del tenant propietario.
    /// </summary>
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Nombre del tenant (proyectado vía JOIN, solo display).
    /// </summary>
    public string? CompanyName { get; init; }

    /// <summary>
    /// Valor crudo de <c>GcRecord</c> — base del flag <see cref="IsDeleted"/>.
    /// </summary>
    public int GcRecord { get; init; }

    /// <summary>
    /// True cuando la fila fue soft-deleted.
    /// </summary>
    public bool IsDeleted => GcRecord != 0;

    /// <summary>
    /// Bitmask crudo de tipos permitidos — la lista legible está en
    /// <see cref="AllowedDocumentTypeNames"/>. Se persiste como <c>int</c>
    /// para que el DTO no necesite referenciar tipos del Domain layer.
    /// </summary>
    public int AllowedDocumentTypes { get; init; }

    /// <summary>
    /// Lista legible de tipos permitidos derivada del bitmask. Útil para
    /// mostrar al usuario "Pdf, Word, Image" en vez de un entero opaco.
    /// Los valores coinciden con los nombres de <c>JOIN.Domain.Enums.DocumentType</c>
    /// (declarados como literales porque la capa DTO no puede referenciar
    /// tipos del Domain por convención de dependencias).
    /// </summary>
    public IReadOnlyList<string> AllowedDocumentTypeNames
    {
        get
        {
            var names = new List<string>();
            const int Pdf = 1 << 0;
            const int Word = 1 << 1;
            const int Excel = 1 << 2;
            const int Text = 1 << 3;
            const int Image = 1 << 4;
            const int Other = 1 << 5;

            if ((AllowedDocumentTypes & Pdf) != 0) names.Add("Pdf");
            if ((AllowedDocumentTypes & Word) != 0) names.Add("Word");
            if ((AllowedDocumentTypes & Excel) != 0) names.Add("Excel");
            if ((AllowedDocumentTypes & Text) != 0) names.Add("Text");
            if ((AllowedDocumentTypes & Image) != 0) names.Add("Image");
            if ((AllowedDocumentTypes & Other) != 0) names.Add("Other");

            return names;
        }
    }

    /// <summary>
    /// Tamaño máximo permitido por archivo, en bytes.
    /// </summary>
    public long MaxFileSizeBytes { get; init; }

    /// <summary>
    /// Máximo de adjuntos activos permitidos por ticket.
    /// </summary>
    public int MaxFilesPerTicket { get; init; }

    /// <summary>
    /// Máximo de adjuntos por empresa por día UTC. <c>null</c> significa
    /// "sin límite diario".
    /// </summary>
    public int? MaxFilesPerDay { get; init; }

    /// <summary>
    /// Timestamp UTC de creación.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}