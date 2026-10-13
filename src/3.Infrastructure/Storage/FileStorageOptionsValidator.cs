using JOIN.Domain.Enums;
using Microsoft.Extensions.Configuration;

namespace JOIN.Infrastructure.Storage;

/// <summary>
/// SPEC 49-post: valida que <see cref="FileStorageOptions.Provider"/>
/// apunte a una sección presente y completa. Sin esta guarda, un
/// <c>Provider=AzureBlob</c> sin <c>ContainerName</c> llegaría a la
/// primera subida y devolvería un 500 genérico. La validación se ejecuta
/// en el arranque (<c>ValidateOnStart</c>) para que un error de
/// despliegue se vea al levantar la API, no en producción.
/// </summary>
internal static class FileStorageOptionsValidator
{
    public static bool IsValid(FileStorageOptions options, IConfiguration configuration)
    {
        var provider = options.Provider;
        IConfigurationSection section = provider switch
        {
            StorageProviderKind.Local => configuration.GetSection("FileStorage:Local"),
            StorageProviderKind.AzureBlob => configuration.GetSection("FileStorage:AzureBlob"),
            StorageProviderKind.S3 => configuration.GetSection("FileStorage:S3"),
            StorageProviderKind.CloudflareR2 => configuration.GetSection("FileStorage:CloudflareR2"),
            _ => null!
        };

        if (section is null || !section.Exists())
        {
            return false;
        }

        return provider switch
        {
            StorageProviderKind.Local => !string.IsNullOrWhiteSpace(options.Local.RootPath),
            StorageProviderKind.AzureBlob => options.AzureBlob.IsConfigured,
            StorageProviderKind.S3 => options.S3.IsConfigured,
            StorageProviderKind.CloudflareR2 => options.CloudflareR2.IsConfigured,
            _ => false
        };
    }
}
