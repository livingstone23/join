using JOIN.Domain.Enums;

namespace JOIN.Infrastructure.Storage;

/// <summary>
/// Opciones de binding para el sistema de almacenamiento de adjuntos
/// (SPEC 49-post). Se bindea desde la sección <c>FileStorage</c> de
/// <c>appsettings.json</c> o de variables de entorno
/// (<c>FileStorage__Provider</c>, <c>FileStorage__AzureBlob__...</c>, etc.).
/// El switch entre proveedores se hace solo por configuración, sin
/// recompilar: <see cref="Provider"/> decide quién recibe las subidas
/// nuevas; las secciones de los demás proveedores pueden seguir
/// presentes para que los adjuntos viejos sigan leyéndose desde su
/// servicio original.
/// </summary>
public sealed class FileStorageOptions
{
    /// <summary>
    /// Proveedor activo — recibe todas las subidas nuevas. Default
    /// <c>Local</c>; ver <see cref="StorageProviderKind"/>.
    /// </summary>
    public StorageProviderKind Provider { get; set; } = StorageProviderKind.Local;

    /// <summary>
    /// Configuración del adapter de disco local
    /// (<see cref="Local.LocalDiskFileStorageAdapter"/>). Se registra
    /// siempre, no solo cuando <see cref="Provider"/> es <c>Local</c>:
    /// es el de pruebas y permite seguir leyendo adjuntos locales
    /// históricos después de pasar a la nube.
    /// </summary>
    public LocalStorageOptions Local { get; set; } = new();

    /// <summary>
    /// Configuración del adapter de Azure Blob Storage
    /// (<see cref="AzureBlob.AzureBlobFileStorageAdapter"/>). Solo se
    /// registra si la sección está completa (ver
    /// <see cref="AzureBlobStorageOptions.IsConfigured"/>); en caso
    /// contrario, cualquier fila con <c>StorageProvider = AzureBlob</c>
    /// devuelve 503 <c>STORAGE_PROVIDER_NOT_CONFIGURED</c> al descargar.
    /// </summary>
    public AzureBlobStorageOptions AzureBlob { get; set; } = new();

    /// <summary>
    /// Configuración del adapter compatible con Amazon S3
    /// (<see cref="S3.S3CompatibleFileStorageAdapter"/>) para AWS S3.
    /// </summary>
    public S3StorageOptions S3 { get; set; } = new();

    /// <summary>
    /// Configuración del adapter para Cloudflare R2, servido por la
    /// misma clase que S3. R2 implementa la API de S3, así que un
    /// único adapter con <see cref="S3StorageOptions.ServiceUrl"/>
    /// configurable cubre ambos casos; la fila distingue cuál de los
    /// dos guardó el archivo vía <see cref="StorageProviderKind"/>.
    /// </summary>
    public CloudflareR2StorageOptions CloudflareR2 { get; set; } = new();
}

/// <summary>
/// Opciones del adapter de disco local. <c>RootPath</c> se interpreta
/// como raíz del filesystem; todas las claves de almacenamiento se
/// resuelven relativas a este path con guard de path traversal
/// (<c>..</c>). En Docker se monta un volumen nombrado sobre esta
/// carpeta; ver <c>docker-compose.yml</c>.
/// </summary>
public sealed class LocalStorageOptions
{
    /// <summary>
    /// Carpeta raíz del filesystem donde se persisten los adjuntos.
    /// Default <c>App_Data/ticket-attachments</c> (relativa al working
    /// directory del proceso). En entornos con múltiples réplicas de la
    /// API, este path debe apuntar a un volumen compartido (NFS, Azure
    /// Files, etc.).
    /// </summary>
    public string RootPath { get; set; } = "App_Data/ticket-attachments";
}

/// <summary>
/// Opciones del adapter de Azure Blob Storage.
/// </summary>
public sealed class AzureBlobStorageOptions
{
    /// <summary>
    /// Cadena de conexión de Azure Storage
    /// (<c>DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...</c>).
    /// No va en <c>appsettings.json</c> versionado: se carga por
    /// variable de entorno o user-secrets.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Nombre del contenedor donde se guardan los blobs. Cada
    /// <c>storageKey</c> es el nombre del blob dentro del contenedor.
    /// </summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>
    /// Si <c>true</c>, el adapter crea el contenedor al primer <c>SaveAsync</c>
    /// cuando no existe todavía. <c>false</c> en producción: el
    /// contenedor es infraestructura administrada fuera del ciclo de
    /// la API.
    /// </summary>
    public bool CreateContainerIfNotExists { get; set; }

    /// <summary>
    /// La sección se considera configurada cuando tiene
    /// <see cref="ConnectionString"/> y <see cref="ContainerName"/> no
    /// vacíos. El DI usa esto para registrar el adapter con clave y
    /// validar al arranque.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ConnectionString)
        && !string.IsNullOrWhiteSpace(ContainerName);
}

/// <summary>
/// Opciones del adapter compatible con S3 (AWS S3 y, opcionalmente,
/// cualquier servicio compatible vía <see cref="ServiceUrl"/> — MinIO,
/// etc.). Para Cloudflare R2 ver <see cref="CloudflareR2StorageOptions"/>.
/// </summary>
public sealed class S3StorageOptions
{
    public string BucketName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>
    /// URL del servicio S3-compatiblle. Vacío para AWS S3 (usa el
    /// endpoint por defecto de la región). Útil para apuntar a MinIO o
    /// a un emulador en pruebas.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// <c>true</c> para usar path-style addressing (MinIO y la mayoría
    /// de emuladores). AWS S3 usa virtual-hosted-style; dejar
    /// <c>false</c> en producción.
    /// </summary>
    public bool ForcePathStyle { get; set; }

    /// <summary>
    /// La sección se considera configurada cuando tiene bucket,
    /// región, access key y secret key no vacíos.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BucketName)
        && !string.IsNullOrWhiteSpace(Region)
        && !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey);
}

/// <summary>
/// Opciones del adapter para Cloudflare R2. R2 implementa la API de
/// S3, así que se sirve desde <see cref="S3.S3CompatibleFileStorageAdapter"/>
/// con un endpoint fijo (<c>https://{AccountId}.r2.cloudflarestorage.com</c>)
/// y región <c>auto</c>.
/// </summary>
public sealed class CloudflareR2StorageOptions
{
    /// <summary>
    /// Account ID de Cloudflare (visible en el dashboard de R2). El
    /// adapter arma <c>ServiceUrl = https://{AccountId}.r2.cloudflarestorage.com</c>.
    /// </summary>
    public string AccountId { get; set; } = string.Empty;

    public string BucketName { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>
    /// La sección se considera configurada cuando los cuatro campos
    /// obligatorios están presentes. El DI falla rápido al arrancar si
    /// <see cref="FileStorageOptions.Provider"/> apunta a R2 con la
    /// sección incompleta (ver <c>DependencyInjection.AddInfrastructureServices</c>).
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountId)
        && !string.IsNullOrWhiteSpace(BucketName)
        && !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey);
}
