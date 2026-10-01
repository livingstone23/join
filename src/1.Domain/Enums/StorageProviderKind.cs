namespace JOIN.Domain.Enums;

/// <summary>
/// Proveedor de almacenamiento físico detrás del cual vive el archivo
/// representado por un <c>TicketDocument</c>. Cada fila guarda su propio
/// proveedor para que una migración futura (disco local → Azure Blob, etc.)
/// no requiera reescribir las filas históricas — un archivo viejo subido a
/// Local sigue resolviendo a Local aunque las filas nuevas vayan a Blob.
/// </summary>
public enum StorageProviderKind
{
    /// <summary>
    /// Disco local del servidor (único adapter implementado en esta spec;
    /// ver <c>LocalDiskFileStorageAdapter</c>).
    /// </summary>
    Local = 0,

    /// <summary>
    /// Azure Blob Storage — seam listo, adapter NO implementado en esta spec.
    /// </summary>
    AzureBlob = 1,

    /// <summary>
    /// Amazon S3 — seam listo, adapter NO implementado en esta spec.
    /// </summary>
    S3 = 2
}