namespace JOIN.Infrastructure.Storage;

/// <summary>
/// Guarda de claves de almacenamiento común a los cuatro adapters
/// (Local, Azure Blob, S3, R2). Rechaza claves vacías, absolutas o que
/// intenten escapar de la raíz vía segmentos <c>..</c> (con separador
/// <c>/</c> o <c>\</c>) antes de que el adapter toque el servicio
/// subyacente. La verificación final de que la ruta resuelta sigue
/// dentro del <c>RootPath</c> es responsabilidad del adapter local
/// (no aplica a la nube: el bucket o contenedor ya hace de sandbox).
/// </summary>
internal static class StorageKeyGuard
{
    /// <summary>
    /// Lanza <see cref="InvalidOperationException"/> si la clave es
    /// inválida. Devuelve la lista de segmentos ya normalizados
    /// (separadores <c>/</c> y <c>\</c>), útil para que el adapter
    /// local los encadene con su <c>RootPath</c> vía
    /// <see cref="System.IO.Path.Combine(string[])"/>.
    /// </summary>
    public static string[] EnsureSafeKey(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new InvalidOperationException("Storage key must not be empty.");
        }

        var segments = storageKey.Split('/', '\\');

        // Path.IsPathRooted es dependiente de la plataforma: en Linux
        // "\\foo" no es rooted, pero eso no significa que sea seguro
        // (sería un blob "\foo" en S3 o un subdirectorio "etc" en
        // Local). Tratamos cualquier clave que arranque con / o \ como
        // absoluta en cualquier plataforma.
        var isAbsolute =
            System.IO.Path.IsPathRooted(storageKey)
            || storageKey.StartsWith('/')
            || storageKey.StartsWith('\\');

        if (isAbsolute || segments.Any(segment => segment == ".."))
        {
            throw new InvalidOperationException(
                $"Storage key '{storageKey}' resolves outside the configured root.");
        }

        return segments;
    }
}
