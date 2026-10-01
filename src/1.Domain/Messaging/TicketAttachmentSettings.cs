using JOIN.Domain.Audit;
using JOIN.Domain.Enums;

namespace JOIN.Domain.Messaging;

/// <summary>
/// Configuración por empresa que gobierna el comportamiento del módulo de
/// adjuntos de tickets: tipos permitidos, tamaño máximo por archivo, máximo
/// de archivos por ticket, y cuota diaria opcional. Vive junto a
/// <c>TicketCompanyDefault</c> — es el mismo tipo de "configuración singleton
/// por empresa" del módulo de tickets.
/// </summary>
public class TicketAttachmentSettings : BaseTenantEntity
{
    /// <summary>
    /// Bitmask de tipos de documento que esta empresa acepta. Un valor
    /// <see cref="DocumentType.None"/> deshabilita toda subida sin necesidad
    /// de un flag separado (todo chequeo HasFlag falla por construcción).
    /// </summary>
    public DocumentType AllowedDocumentTypes { get; set; }

    /// <summary>
    /// Tamaño máximo permitido por archivo, en bytes. Una subida con
    /// <c>Length &gt; MaxFileSizeBytes</c> se rechaza con FILE_TOO_LARGE.
    /// </summary>
    public long MaxFileSizeBytes { get; set; }

    /// <summary>
    /// Máximo de adjuntos activos permitidos por ticket. Alcanzar este
    /// límite rechaza nuevas subidas con MAX_FILES_PER_TICKET_REACHED (409).
    /// </summary>
    public int MaxFilesPerTicket { get; set; }

    /// <summary>
    /// Máximo de adjuntos por empresa por día calendario UTC. <c>null</c>
    /// significa "sin límite diario". Cuando se define y se alcanza, las
    /// nuevas subidas se rechazan con DAILY_ATTACHMENT_QUOTA_REACHED (429).
    /// </summary>
    public int? MaxFilesPerDay { get; set; }
}