using JOIN.Domain.Enums;
using Microsoft.Extensions.Options;

namespace JOIN.Infrastructure.Storage;

/// <summary>
/// SPEC 49-post: valida que <see cref="FileStorageOptions.Provider"/>
/// apunte a una sección completa. Sin esta guarda, un
/// <c>Provider=AzureBlob</c> sin <c>ContainerName</c> llegaría a la
/// primera subida y devolvería un 500 genérico. Se ejecuta en el
/// arranque (<c>ValidateOnStart</c>) para que un error de despliegue se
/// vea al levantar la API, y el mensaje nombra cada clave faltante.
/// Las secciones de los proveedores no activos no se validan: si están
/// incompletas, simplemente no se registran.
/// </summary>
internal sealed class FileStorageOptionsValidator : IValidateOptions<FileStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, FileStorageOptions options)
    {
        var missing = GetMissingKeys(options);
        return missing.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"FileStorage:Provider is '{options.Provider}' but its section is incomplete. Missing: {string.Join(", ", missing)}.");
    }

    /// <summary>
    /// Claves de configuración que faltan para el proveedor activo, con
    /// la ruta completa (<c>FileStorage:AzureBlob:ContainerName</c>).
    /// </summary>
    internal static IReadOnlyList<string> GetMissingKeys(FileStorageOptions options)
    {
        var missing = new List<string>();

        void Require(string value, string key)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                missing.Add(key);
            }
        }

        switch (options.Provider)
        {
            case StorageProviderKind.Local:
                Require(options.Local.RootPath, "FileStorage:Local:RootPath");
                break;
            case StorageProviderKind.AzureBlob:
                Require(options.AzureBlob.ConnectionString, "FileStorage:AzureBlob:ConnectionString");
                Require(options.AzureBlob.ContainerName, "FileStorage:AzureBlob:ContainerName");
                break;
            case StorageProviderKind.S3:
                Require(options.S3.BucketName, "FileStorage:S3:BucketName");
                Require(options.S3.Region, "FileStorage:S3:Region");
                Require(options.S3.AccessKeyId, "FileStorage:S3:AccessKeyId");
                Require(options.S3.SecretAccessKey, "FileStorage:S3:SecretAccessKey");
                break;
            case StorageProviderKind.CloudflareR2:
                Require(options.CloudflareR2.AccountId, "FileStorage:CloudflareR2:AccountId");
                Require(options.CloudflareR2.BucketName, "FileStorage:CloudflareR2:BucketName");
                Require(options.CloudflareR2.AccessKeyId, "FileStorage:CloudflareR2:AccessKeyId");
                Require(options.CloudflareR2.SecretAccessKey, "FileStorage:CloudflareR2:SecretAccessKey");
                break;
            default:
                missing.Add($"FileStorage:Provider (unknown value '{options.Provider}')");
                break;
        }

        return missing;
    }
}
