namespace JOIN.Application.Interface;

/// <summary>
/// Contrato agnóstico de proveedor para almacenamiento de archivos binarios.
/// El Application layer opera contra esta interfaz; ningún tipo de
/// <c>Microsoft.AspNetCore.*</c> ni de un SDK de nube debe aparecer en su
/// firma ni en los callers — mismo criterio que ya mantiene
/// <see cref="IEmailService"/>. Cada fila de <c>TicketDocument</c> guarda
/// su propio <c>StorageProviderKind</c>, así que este contrato es estable
/// cuando se agreguen adapters de Azure Blob o S3.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Persiste el contenido del stream bajo el <paramref name="storageKey"/>
    /// indicado y devuelve el resultado (la misma clave más el tamaño
    /// observado en bytes). El handler es quien construye
    /// <paramref name="storageKey"/> — no se le pregunta al adapter qué
    /// nombre usó, así la base de datos y el storage quedan sincronizados
    /// sin un round-trip extra.
    /// </summary>
    /// <param name="content">Stream del archivo. La implementación es dueña
    /// de su ciclo de vida y debe copiar/disponer según corresponda.</param>
    /// <param name="storageKey">Clave relativa al root del provider, decidida
    /// por el handler.</param>
    /// <param name="contentType">MIME type del archivo (informativo —
    /// algunas plataformas lo usan para inferir el blob content-type).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task<FileStorageSaveResult> SaveAsync(
        Stream content,
        string storageKey,
        string contentType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Abre el archivo para lectura. Devuelve <c>null</c> cuando la fila
    /// existe en metadata pero el archivo físico no — el caller debe
    /// traducir este caso a <c>FILE_NOT_FOUND_IN_STORAGE</c> (404) sin
    /// generar un 500 genérico.
    /// </summary>
    /// <param name="storageKey">Clave relativa al root del provider.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task<Stream?> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Borra el archivo físico. Devuelve <c>false</c> si no existía.
    /// Implementado y testeado aunque el flujo de soft-delete de metadata
    /// de esta spec no lo invoque — queda disponible para una spec de
    /// retención/limpieza futura sin tocar el contrato.
    /// </summary>
    /// <param name="storageKey">Clave relativa al root del provider.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task<bool> DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resultado de un <see cref="IFileStorageService.SaveAsync"/>. El handler
/// persiste estos dos valores en la fila de <c>TicketDocument</c> —
/// <c>StorageKey</c> se guarda en <c>Path</c> y <c>SizeBytes</c> en
/// <c>SizeBytes</c>.
/// </summary>
/// <param name="StorageKey">Clave final con la que quedó guardado el archivo.</param>
/// <param name="SizeBytes">Tamaño observado en bytes al momento de la copia.</param>
public sealed record FileStorageSaveResult(string StorageKey, long SizeBytes);