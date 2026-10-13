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
    /// Disco local del servidor (<c>LocalDiskFileStorageAdapter</c>).
    /// </summary>
    Local = 0,

    /// <summary>
    /// Azure Blob Storage (<c>AzureBlobFileStorageAdapter</c>).
    /// </summary>
    AzureBlob = 1,

    /// <summary>
    /// Amazon S3 (<c>S3CompatibleFileStorageAdapter</c>).
    /// </summary>
    S3 = 2,

    /// <summary>
    /// Cloudflare R2, también servido por <c>S3CompatibleFileStorageAdapter</c>
    /// con un <c>Kind</c> distinto: R2 implementa la API de S3, pero
    /// un adjunto guardado en R2 vive en otro servicio y con otras
    /// credenciales que uno en AWS, así que la fila debe recordar cuál.
    /// </summary>
    CloudflareR2 = 3
}
