using JOIN.Application.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JOIN.Infrastructure.Storage.Local;

/// <summary>
/// Implementación de <see cref="IFileStorageService"/> sobre el filesystem
/// del servidor. Único adapter de storage implementado en esta spec —
/// el contrato queda listo para un futuro <c>AzureBlobFileStorageAdapter</c>
/// o <c>S3FileStorageAdapter</c> cuando negocio decida a qué nube migrar.
///
/// Toda <c>storageKey</c> se valida con <see cref="ResolveSafePath"/> antes
/// de tocar el filesystem: si la ruta resuelta escapa del <c>RootPath</c>
/// configurado (por ejemplo por un segmento <c>..</c>), el adapter lanza
/// <see cref="InvalidOperationException"/> sin escribir nada.
/// </summary>
public sealed class LocalDiskFileStorageAdapter(
    IOptions<FileStorageOptions> options,
    ILogger<LocalDiskFileStorageAdapter> logger)
    : IFileStorageService
{
    private readonly FileStorageOptions _options = options.Value;
    private readonly ILogger<LocalDiskFileStorageAdapter> _logger = logger;

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
    /// Combina <paramref name="storageKey"/> con <c>RootPath</c> y valida
    /// que el resultado siga dentro de la raíz. Lanza
    /// <see cref="InvalidOperationException"/> ante cualquier intento de
    /// path traversal (<c>..</c>, segmentos absolutos, etc.).
    /// </summary>
    private string ResolveSafePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new InvalidOperationException("Storage key must not be empty.");
        }

        var root = Path.GetFullPath(_options.RootPath);
        var combined = Path.GetFullPath(Path.Combine(root, storageKey));

        if (!combined.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Storage key '{storageKey}' resolves outside the configured root.");
        }

        return combined;
    }
}