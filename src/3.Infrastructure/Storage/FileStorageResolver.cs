using JOIN.Application.Interface;
using JOIN.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JOIN.Infrastructure.Storage;

/// <summary>
/// Implementación de <see cref="IFileStorageResolver"/> que delega a
/// los adapters registrados con clave por <see cref="StorageProviderKind"/>
/// (SPEC 49-post). El <see cref="Current"/> siempre resuelve al
/// provider activo de <see cref="FileStorageOptions.Provider"/>; el
/// <see cref="Resolve"/> puede devolver <c>null</c> si la fila apunta
/// a un provider cuya sección no está configurada (el handler traduce
/// eso a <c>STORAGE_PROVIDER_NOT_CONFIGURED</c>).
/// </summary>
internal sealed class FileStorageResolver : IFileStorageResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly FileStorageOptions _options;
    private readonly ILogger<FileStorageResolver> _logger;

    public FileStorageResolver(
        IServiceProvider serviceProvider,
        IOptions<FileStorageOptions> options,
        ILogger<FileStorageResolver> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public IFileStorageService Current => Resolve(_options.Provider)
        ?? throw new InvalidOperationException(
            $"FileStorage provider '{_options.Provider}' is not configured. " +
            "Check that the corresponding section in appsettings or environment variables is complete.");

    /// <inheritdoc />
    public IFileStorageService? Resolve(StorageProviderKind kind)
    {
        // Local siempre está registrado: el resolver.Current puede
        // ser otro, pero Local queda como fallback para que los
        // adjuntos viejos sigan descargándose (ver SPEC 49-post).
        var keyed = _serviceProvider.GetKeyedService<IFileStorageService>(kind);
        if (keyed is null)
        {
            _logger.LogWarning(
                "No IFileStorageService registered for StorageProviderKind.{Kind}. " +
                "Rows saved with this provider will return 503 STORAGE_PROVIDER_NOT_CONFIGURED until the section is restored.",
                kind);
        }
        return keyed;
    }
}
