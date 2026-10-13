using JOIN.Domain.Enums;

namespace JOIN.Application.Interface;

/// <summary>
/// Punto único de entrada al storage desde el Application layer. El
/// resolver se inyecta en lugar de un <see cref="IFileStorageService"/>
/// concreto, así el handler:
/// <list type="bullet">
///   <item>toma <see cref="Current"/> para subir — y graba su
///   <see cref="IFileStorageService.Kind"/> en la fila,</item>
///   <item>toma <see cref="Resolve"/> para descargar — usando el
///   <see cref="StorageProviderKind"/> que la propia fila guarda, no el
///   proveedor configurado en el entorno.</item>
/// </list>
/// De este modo, cambiar de local a nube no obliga a migrar archivos:
/// cada adjunto se sigue descargando desde el servicio donde se guardó.
/// </summary>
public interface IFileStorageResolver
{
    /// <summary>
    /// Adapter que recibe las subidas nuevas. Es el que se selecciona
    /// en <c>FileStorage:Provider</c>.
    /// </summary>
    IFileStorageService Current { get; }

    /// <summary>
    /// Adapter que guardó un adjunto existente. Devuelve <c>null</c>
    /// si el proveedor de la fila no está configurado en este entorno
    /// (por ejemplo, la sección <c>FileStorage:CloudflareR2</c> se quitó
    /// pero la base todavía tiene filas de R2). El handler traduce ese
    /// <c>null</c> a <c>STORAGE_PROVIDER_NOT_CONFIGURED</c> (503).
    /// </summary>
    IFileStorageService? Resolve(StorageProviderKind kind);
}
