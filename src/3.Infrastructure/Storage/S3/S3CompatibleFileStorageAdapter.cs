using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using JOIN.Application.Interface;
using JOIN.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JOIN.Infrastructure.Storage.S3;

/// <summary>
/// Adapter de <see cref="IFileStorageService"/> compatible con la API
/// de Amazon S3 (SPEC 49-post). Una sola clase cubre tanto AWS S3
/// como Cloudflare R2: R2 implementa la API de S3, así que la
/// diferencia entre los dos vive en la configuración
/// (<see cref="FileStorageOptions.S3"/> y
/// <see cref="FileStorageOptions.CloudflareR2"/>) y en el
/// <see cref="StorageProviderKind"/> que este adapter reporta. La fila
/// guarda ese <c>Kind</c> y <see cref="JOIN.Application.Interface.IFileStorageResolver"/>
/// lo usa para devolver este mismo adapter con el <c>Kind</c> que
/// corresponda a la sección de la fila.
/// </summary>
public sealed class S3CompatibleFileStorageAdapter : IFileStorageService
{
    private readonly IAmazonS3 _client;
    private readonly string _bucketName;
    private readonly StorageProviderKind _kind;
    private readonly ILogger _logger;

    /// <summary>
    /// Constructor de producción. Se le pasa el <see cref="IAmazonS3"/>
    /// ya configurado por el DI según la sección de
    /// <see cref="FileStorageOptions"/> que corresponda.
    /// </summary>
    public S3CompatibleFileStorageAdapter(
        IOptions<FileStorageOptions> options,
        ILogger<S3CompatibleFileStorageAdapter> s3Logger,
        StorageProviderKind kind)
    {
        _kind = kind;
        _logger = s3Logger;
        _bucketName = kind == StorageProviderKind.CloudflareR2
            ? options.Value.CloudflareR2.BucketName
            : options.Value.S3.BucketName;
        _client = BuildClient(options.Value, kind);
    }

    /// <summary>
    /// Constructor para tests unitarios: acepta un cliente
    /// <see cref="IAmazonS3"/> simulado.
    /// </summary>
    public S3CompatibleFileStorageAdapter(
        IAmazonS3 client,
        string bucketName,
        StorageProviderKind kind,
        ILogger logger)
    {
        _client = client;
        _bucketName = bucketName;
        _kind = kind;
        _logger = logger;
    }

    /// <inheritdoc />
    public StorageProviderKind Kind => _kind;

    /// <inheritdoc />
    public async Task<FileStorageSaveResult> SaveAsync(
        Stream content,
        string storageKey,
        string contentType,
        CancellationToken cancellationToken)
    {
        var segments = StorageKeyGuard.EnsureSafeKey(storageKey);
        var key = string.Join('/', segments);

        // El SDK v4 detecta el ContentLength del InputStream; cuando el
        // stream de origen no es seekable, lo copiamos a un MemoryStream
        // para poder reportar el tamaño observado en FileStorageSaveResult
        // (mismo criterio que LocalDisk y AzureBlob).
        long size;
        Stream uploadStream;
        if (content.CanSeek)
        {
            size = content.Length - content.Position;
            uploadStream = content;
        }
        else
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            size = buffer.Length;
            buffer.Position = 0;
            uploadStream = buffer;
        }

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = uploadStream,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType
        };

        await _client.PutObjectAsync(request, cancellationToken);
        return new FileStorageSaveResult(storageKey, size);
    }

    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        var segments = StorageKeyGuard.EnsureSafeKey(storageKey);
        var key = string.Join('/', segments);

        try
        {
            var response = await _client.GetObjectAsync(_bucketName, key, cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound || ex.ErrorCode == "NoSuchKey")
        {
            _logger.LogWarning("Object not found in S3 storage: {StorageKey}", storageKey);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        var segments = StorageKeyGuard.EnsureSafeKey(storageKey);
        var key = string.Join('/', segments);

        // S3 no informa si el objeto existía: DeleteObject es idempotente
        // y devuelve 204 tanto para un objeto existente como para uno
        // ausente. Devolvemos true salvo que el SDK lance (permisos,
        // red, etc.).
        await _client.DeleteObjectAsync(_bucketName, key, cancellationToken);
        return true;
    }

    private static IAmazonS3 BuildClient(FileStorageOptions options, StorageProviderKind kind)
    {
        var credentials = kind == StorageProviderKind.CloudflareR2
            ? new BasicAWSCredentials(
                options.CloudflareR2.AccessKeyId,
                options.CloudflareR2.SecretAccessKey)
            : new BasicAWSCredentials(
                options.S3.AccessKeyId,
                options.S3.SecretAccessKey);

        var region = kind == StorageProviderKind.CloudflareR2
            ? RegionEndpoint.USEast1 // R2 acepta cualquier region; "auto" no es un endpoint válido del SDK
            : RegionEndpoint.GetBySystemName(options.S3.Region);

        var clientConfig = new AmazonS3Config
        {
            // AWSSDK.S3 v4 expone la región vía RegionEndpoint (no
            // existe `Region = string` en esta versión).
            RegionEndpoint = region,
            ForcePathStyle = kind == StorageProviderKind.S3 && options.S3.ForcePathStyle
        };

        if (kind == StorageProviderKind.CloudflareR2)
        {
            // R2 siempre usa su propio ServiceUrl.
            var accountId = options.CloudflareR2.AccountId;
            clientConfig.ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com";
        }
        else if (!string.IsNullOrWhiteSpace(options.S3.ServiceUrl))
        {
            // MinIO u otro servicio compatible con S3.
            clientConfig.ServiceURL = options.S3.ServiceUrl;
        }

        return new AmazonS3Client(credentials, clientConfig);
    }
}
