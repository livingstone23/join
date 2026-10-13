using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using JOIN.Application.Interface;
using JOIN.Domain.Enums;
using Microsoft.Extensions.Logging;

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
/// devuelve la instancia registrada para esa sección. El DI registra
/// una instancia por sección (singleton con clave): los clientes del
/// SDK son thread-safe y están pensados para reutilizarse.
/// </summary>
public sealed class S3CompatibleFileStorageAdapter : IFileStorageService
{
    private readonly IAmazonS3 _client;
    private readonly string _bucketName;
    private readonly StorageProviderKind _kind;
    private readonly ILogger _logger;

    /// <summary>
    /// Construye el adapter con su propio <see cref="IAmazonS3"/>. En
    /// producción el DI lo crea vía <see cref="Create"/>; los tests
    /// unitarios pasan un cliente simulado.
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

    /// <summary>
    /// Fábrica de producción: arma el cliente S3 con las credenciales,
    /// el endpoint y el bucket de la sección que corresponde a
    /// <paramref name="kind"/> (<c>S3</c> o <c>CloudflareR2</c>).
    /// </summary>
    public static S3CompatibleFileStorageAdapter Create(
        FileStorageOptions options,
        StorageProviderKind kind,
        ILogger logger)
    {
        var isR2 = kind == StorageProviderKind.CloudflareR2;
        var credentials = isR2
            ? new BasicAWSCredentials(options.CloudflareR2.AccessKeyId, options.CloudflareR2.SecretAccessKey)
            : new BasicAWSCredentials(options.S3.AccessKeyId, options.S3.SecretAccessKey);
        var bucketName = isR2 ? options.CloudflareR2.BucketName : options.S3.BucketName;

        return new S3CompatibleFileStorageAdapter(
            new AmazonS3Client(credentials, BuildClientConfig(options, kind)),
            bucketName,
            kind,
            logger);
    }

    /// <inheritdoc />
    public StorageProviderKind Kind => _kind;

    /// <summary>Bucket de la sección con la que se construyó el adapter.</summary>
    internal string BucketName => _bucketName;

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
        // El buffer temporal vive hasta que termina PutObjectAsync.
        await using var buffer = content.CanSeek ? null : new MemoryStream();
        var uploadStream = content;
        if (buffer is not null)
        {
            await content.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            uploadStream = buffer;
        }

        var size = uploadStream.Length - uploadStream.Position;

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = uploadStream,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            // R2 no soporta la firma de payload por chunks (aws-chunked)
            // que el SDK usa por defecto para PutObject.
            DisablePayloadSigning = _kind == StorageProviderKind.CloudflareR2
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

    /// <summary>
    /// Configuración del cliente según el proveedor. R2 usa el endpoint
    /// <c>https://{AccountId}.r2.cloudflarestorage.com</c>, región de
    /// firma <c>auto</c> y checksums solo cuando la operación los exige
    /// (R2 rechaza los encabezados de checksum que el SDK v4 envía por
    /// defecto). S3 usa la región configurada y, si hay
    /// <c>ServiceUrl</c> (MinIO u otro compatible), ese endpoint firmado
    /// con la misma región.
    /// </summary>
    internal static AmazonS3Config BuildClientConfig(FileStorageOptions options, StorageProviderKind kind)
    {
        if (kind == StorageProviderKind.CloudflareR2)
        {
            return new AmazonS3Config
            {
                ServiceURL = $"https://{options.CloudflareR2.AccountId}.r2.cloudflarestorage.com",
                AuthenticationRegion = "auto",
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            };
        }

        var config = new AmazonS3Config { ForcePathStyle = options.S3.ForcePathStyle };
        if (string.IsNullOrWhiteSpace(options.S3.ServiceUrl))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.S3.Region);
        }
        else
        {
            // ServiceURL y RegionEndpoint son excluyentes en el SDK: con
            // endpoint propio, la región solo se usa para firmar.
            config.ServiceURL = options.S3.ServiceUrl;
            config.AuthenticationRegion = options.S3.Region;
        }

        return config;
    }
}
