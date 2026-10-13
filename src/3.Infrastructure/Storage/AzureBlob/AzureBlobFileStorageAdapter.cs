using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using JOIN.Application.Interface;
using JOIN.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JOIN.Infrastructure.Storage.AzureBlob;

/// <summary>
/// Adapter de <see cref="IFileStorageService"/> sobre Azure Blob
/// Storage (SPEC 49-post). Un contenedor (<c>ContainerName</c>); la
/// <c>storageKey</c> se usa directamente como nombre del blob dentro
/// del contenedor. <c>SaveAsync</c> sube con el <c>ContentType</c> en
/// los headers del blob y crea el contenedor solo cuando
/// <c>CreateContainerIfNotExists = true</c>. <c>OpenReadAsync</c>
/// devuelve <c>null</c> ante 404 del servicio; cualquier otro error
/// se propaga.
/// </summary>
public sealed class AzureBlobFileStorageAdapter : IFileStorageService
{
    private readonly AzureBlobStorageOptions _options;
    private readonly ILogger<AzureBlobFileStorageAdapter> _logger;
    private readonly Lazy<BlobContainerClient> _container;

    /// <summary>
    /// Constructor de producción. El <see cref="BlobContainerClient"/>
    /// se construye a partir de la cadena de conexión + nombre del
    /// contenedor.
    /// </summary>
    public AzureBlobFileStorageAdapter(
        IOptions<FileStorageOptions> options,
        ILogger<AzureBlobFileStorageAdapter> logger)
        : this(options, logger, BuildContainerFromOptions(options.Value.AzureBlob))
    {
    }

    /// <summary>
    /// Constructor de testing. Acepta un factory de
    /// <see cref="BlobContainerClient"/> para que el test pueda
    /// pasar un cliente simulado sin tocar la red.
    /// </summary>
    internal AzureBlobFileStorageAdapter(
        IOptions<FileStorageOptions> options,
        ILogger<AzureBlobFileStorageAdapter> logger,
        Func<BlobContainerClient> containerFactory)
    {
        _options = options.Value.AzureBlob;
        _logger = logger;
        // Se crea una vez y se reutiliza: los clientes del SDK son
        // thread-safe y el adapter se registra como singleton con clave.
        _container = new Lazy<BlobContainerClient>(containerFactory);
    }

    private static Func<BlobContainerClient> BuildContainerFromOptions(AzureBlobStorageOptions options)
    {
        return () =>
        {
            var serviceClient = new BlobServiceClient(options.ConnectionString);
            return serviceClient.GetBlobContainerClient(options.ContainerName);
        };
    }

    /// <inheritdoc />
    public StorageProviderKind Kind => StorageProviderKind.AzureBlob;

    /// <inheritdoc />
    public async Task<FileStorageSaveResult> SaveAsync(
        Stream content,
        string storageKey,
        string contentType,
        CancellationToken cancellationToken)
    {
        var segments = StorageKeyGuard.EnsureSafeKey(storageKey);
        var blobName = string.Join('/', segments);
        var container = _container.Value;

        if (_options.CreateContainerIfNotExists)
        {
            await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        }

        var blob = container.GetBlobClient(blobName);

        var headers = new BlobHttpHeaders
        {
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType
        };

        // UploadAsync sobreescribe si ya existe — coincide con la
        // semántica de SaveAsync (la storageKey la construye el handler
        // y es única por timestamp+guid).
        await blob.UploadAsync(content, new BlobUploadOptions { HttpHeaders = headers }, cancellationToken);

        // Azure no expone el tamaño observado tan directamente: se
        // puede releer el blob, pero para mantener una sola llamada
        // (mismo criterio que LocalDisk y S3) usamos el largo del
        // stream de origen cuando está disponible; si no, 0.
        var observedSize = content.CanSeek ? content.Length - content.Position : 0L;
        return new FileStorageSaveResult(storageKey, observedSize);
    }

    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        var segments = StorageKeyGuard.EnsureSafeKey(storageKey);
        var blobName = string.Join('/', segments);
        var container = _container.Value;
        var blob = container.GetBlobClient(blobName);

        try
        {
            var response = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return response.Value.Content;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Blob not found in Azure storage: {StorageKey}", storageKey);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        var segments = StorageKeyGuard.EnsureSafeKey(storageKey);
        var blobName = string.Join('/', segments);
        var container = _container.Value;
        var blob = container.GetBlobClient(blobName);

        var response = await blob.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        return response.Value;
    }
}
