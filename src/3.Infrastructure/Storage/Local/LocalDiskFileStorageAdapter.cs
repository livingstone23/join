using JOIN.Application.Interface;
using JOIN.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JOIN.Infrastructure.Storage.Local;

/// <summary>
/// Implementación de <see cref="IFileStorageService"/> sobre el filesystem
/// del servidor. Único adapter de storage original (SPEC 36); SPEC 49-post
/// lo conserva con <c>Kind = Local</c> y agrega los adapters de nube
/// (Azure Blob, S3, Cloudflare R2) detrás del mismo
/// <see cref="JOIN.Application.Interface.IFileStorageResolver"/>.
///
/// Toda <c>storageKey</c> se valida con
/// <see cref="StorageKeyGuard.EnsureSafeKey"/> antes de tocar el
/// filesystem y, además, el adapter verifica que la ruta resuelta siga
/// dentro del <c>RootPath</c> configurado (un escape de tipo
/// "<c>&lt;root&gt;-evil</c>" no se atrapa con la guarda de segmentos).
/// </summary>
public sealed class LocalDiskFileStorageAdapter(
    IOptions<FileStorageOptions> options,
    ILogger<LocalDiskFileStorageAdapter> logger)
    : IFileStorageService
{
    private readonly FileStorageOptions _options = options.Value;
    private readonly ILogger<LocalDiskFileStorageAdapter> _logger = logger;

    /// <inheritdoc />
    public StorageProviderKind Kind => StorageProviderKind.Local;

    /// <inheritdoc />
    public async Task<FileStorageSaveResult> SaveAsync(
        Stream content,
        string storageKey,
        string contentType,
        CancellationToken cancellationToken)
    {
        var fullPath = ResolveSafePath(storageKey);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, cancellationToken);

        // Flush + close (await using) antes de leer Length para evitar 0 en
        // plataformas donde el handle mantiene el cursor.
        return new FileStorageSaveResult(storageKey, fileStream.Length);
    }

    /// <inheritdoc />
    public Task<Stream?> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        var fullPath = ResolveSafePath(storageKey);
        if (!File.Exists(fullPath))
        {
            _logger.LogWarning("File not found in local storage: {StorageKey}", storageKey);
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult<Stream?>(stream);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        var fullPath = ResolveSafePath(storageKey);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult(false);
        }

        File.Delete(fullPath);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Combina los segmentos ya validados de <paramref name="storageKey"/>
    /// con <c>RootPath</c> y comprueba que el resultado siga dentro de la
    /// raíz. La validación de segmentos la hace
    /// <see cref="StorageKeyGuard.EnsureSafeKey"/>; este método añade el
    /// segundo check (prefijo-similar) que es específico del filesystem
    /// local — la nube no lo necesita porque el bucket o contenedor
    /// hace de sandbox.
    /// </summary>
    private string ResolveSafePath(string storageKey)
    {
        var segments = StorageKeyGuard.EnsureSafeKey(storageKey);

        var root = Path.GetFullPath(_options.Local.RootPath);
        var combined = Path.GetFullPath(Path.Combine([root, .. segments]));

        // Compare against "root + separator": a bare StartsWith(root) would also
        // accept sibling directories sharing the prefix (e.g. "<root>-evil/...").
        var rootWithSeparator = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Storage key '{storageKey}' resolves outside the configured root.");
        }

        return combined;
    }
}
