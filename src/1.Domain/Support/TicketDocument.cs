using JOIN.Domain.Audit;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;

namespace JOIN.Domain.Support;

/// <summary>
/// Metadata de un documento adjunto a un ticket. El archivo físico vive
/// detrás de un <see cref="StorageProviderKind"/> (disco local en esta spec)
/// y se accede por la combinación de <see cref="Path"/> + provider, no por
/// ruta absoluta — el handler construye el storageKey, no el adapter.
/// Vive junto a <c>TicketLog</c> en <c>Support</c> — mismo criterio de
/// agrupación que ya usa el proyecto para "trazas del ciclo de vida de un
/// ticket".
/// </summary>
public class TicketDocument : BaseTenantEntity
{
    /// <summary>
    /// Ticket al que pertenece el adjunto. Denormalizado (derivable de
    /// <see cref="TicketLogsId"/> vía join) para que el listado por ticket
    /// y el conteo de cupo no paguen un join extra por un dato que no
    /// cambia una vez creada la fila.
    /// </summary>
    public Guid TicketId { get; set; }

    /// <summary>
    /// Log del ciclo de vida del ticket donde se subió el adjunto
    /// (creación, finalización, nota, etc.). Permite trazar exactamente
    /// "en qué momento del ticket" se incorporó cada evidencia.
    /// </summary>
    public Guid TicketLogsId { get; set; }

    /// <summary>
    /// Tipo de documento inferido server-side de la extensión del archivo.
    /// El cliente nunca envía este valor.
    /// </summary>
    public DocumentType DocumentType { get; set; }

    /// <summary>
    /// Nombre original del archivo tal como lo subió el usuario.
    /// Se conserva para la descarga.
    /// </summary>
    public string OriginalName { get; set; } = string.Empty;

    /// <summary>
    /// Nombre físico con el que quedó guardado en el storage — Guid + timestamp
    /// único, formato <c>yyyyMMddHHmmss_&lt;guid-sin-guiones&gt;&lt;ext&gt;</c>.
    /// Único en toda la tabla, no por tenant.
    /// </summary>
    public string NewName { get; set; } = string.Empty;

    /// <summary>
    /// Clave de almacenamiento, relativa al <see cref="StorageProvider"/>
    /// configurado. Formato <c>{companyId}/{ticketId}/{newName}</c>.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Proveedor de almacenamiento contra el que <see cref="Path"/> resuelve.
    /// Cada fila guarda el suyo para que una migración de proveedor no
    /// requiera reescribir las filas históricas.
    /// </summary>
    public StorageProviderKind StorageProvider { get; set; }

    /// <summary>
    /// MIME type del archivo, conservado para servir la descarga con el
    /// <c>Content-Type</c> correcto sin volver a golpear el storage.
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// Tamaño del archivo en bytes — snapshot al momento de la subida.
    /// Permite validar <c>MaxFileSizeBytes</c> sin releer el storage.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Navigation al ticket. FK con <c>Restrict</c> en la config EF.
    /// </summary>
    public virtual Ticket Ticket { get; set; } = null!;

    /// <summary>
    /// Navigation al log del ticket donde se subió. FK con <c>Restrict</c>
    /// en la config EF.
    /// </summary>
    public virtual TicketLog TicketLog { get; set; } = null!;
}