using FluentAssertions;
using JOIN.Domain.Enums;
using JOIN.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;

namespace JOIN.Application.UnitTest.Infrastructure.Storage;

/// <summary>
/// Unit tests for <see cref="FileStorageOptionsValidator"/>. La
/// validación se enchufa a <c>ValidateOnStart</c> en el arranque de
/// la API; estos tests cubren cada branch de
/// <see cref="FileStorageOptions.Provider"/> para garantizar que un
/// provider mal configurado hace fallar el build del host.
/// </summary>
public sealed class FileStorageOptionsValidatorTests
{
    [Fact]
    public void IsValid_WhenLocalProviderAndRootPathSet_ShouldReturnTrue()
    {
        var config = BuildConfig(("FileStorage:Provider", "Local"), ("FileStorage:Local:RootPath", "App_Data/x"));
        var options = new FileStorageOptions
        {
            Provider = StorageProviderKind.Local,
            Local = new LocalStorageOptions { RootPath = "App_Data/x" }
        };

        FileStorageOptionsValidator.IsValid(options, config).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WhenAzureBlobProviderAndSectionComplete_ShouldReturnTrue()
    {
        var config = BuildConfig(
            ("FileStorage:Provider", "AzureBlob"),
            ("FileStorage:AzureBlob:ConnectionString", "DefaultEndpointsProtocol=https;AccountName=x;AccountKey=y;"),
            ("FileStorage:AzureBlob:ContainerName", "ticket-attachments"));
        var options = new FileStorageOptions
        {
            Provider = StorageProviderKind.AzureBlob,
            AzureBlob = new AzureBlobStorageOptions
            {
                ConnectionString = "DefaultEndpointsProtocol=https;AccountName=x;AccountKey=y;",
                ContainerName = "ticket-attachments"
            }
        };

        FileStorageOptionsValidator.IsValid(options, config).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WhenAzureBlobProviderMissingContainerName_ShouldReturnFalse()
    {
        // Falla rápido: la API no debe arrancar si Provider=AzureBlob
        // pero no hay ContainerName. Un error de despliegue se ve al
        // levantar, no en la primera subida.
        var config = BuildConfig(
            ("FileStorage:Provider", "AzureBlob"),
            ("FileStorage:AzureBlob:ConnectionString", "DefaultEndpointsProtocol=https;AccountName=x;AccountKey=y;"));
        var options = new FileStorageOptions
        {
            Provider = StorageProviderKind.AzureBlob,
            AzureBlob = new AzureBlobStorageOptions
            {
                ConnectionString = "DefaultEndpointsProtocol=https;AccountName=x;AccountKey=y;",
                ContainerName = string.Empty
            }
        };

        FileStorageOptionsValidator.IsValid(options, config).Should().BeFalse();
    }

    [Fact]
    public void IsValid_WhenCloudflareR2ProviderMissingAccountId_ShouldReturnFalse()
    {
        var config = BuildConfig(
            ("FileStorage:Provider", "CloudflareR2"),
            ("FileStorage:CloudflareR2:BucketName", "join-attachments"),
            ("FileStorage:CloudflareR2:AccessKeyId", "ak"),
            ("FileStorage:CloudflareR2:SecretAccessKey", "sk"));
        var options = new FileStorageOptions
        {
            Provider = StorageProviderKind.CloudflareR2,
            CloudflareR2 = new CloudflareR2StorageOptions
            {
                AccountId = string.Empty,
                BucketName = "join-attachments",
                AccessKeyId = "ak",
                SecretAccessKey = "sk"
            }
        };

        FileStorageOptionsValidator.IsValid(options, config).Should().BeFalse();
    }

    [Fact]
    public void IsValid_WhenS3ProviderAndSectionComplete_ShouldReturnTrue()
    {
        var config = BuildConfig(
            ("FileStorage:Provider", "S3"),
            ("FileStorage:S3:BucketName", "join-attachments"),
            ("FileStorage:S3:Region", "us-east-1"),
            ("FileStorage:S3:AccessKeyId", "ak"),
            ("FileStorage:S3:SecretAccessKey", "sk"));
        var options = new FileStorageOptions
        {
            Provider = StorageProviderKind.S3,
            S3 = new S3StorageOptions
            {
                BucketName = "join-attachments",
                Region = "us-east-1",
                AccessKeyId = "ak",
                SecretAccessKey = "sk"
            }
        };

        FileStorageOptionsValidator.IsValid(options, config).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WhenSectionDoesNotExist_ShouldReturnFalse()
    {
        var config = BuildConfig(("FileStorage:Provider", "AzureBlob"));
        var options = new FileStorageOptions
        {
            Provider = StorageProviderKind.AzureBlob
        };

        FileStorageOptionsValidator.IsValid(options, config).Should().BeFalse();
    }

    private static IConfiguration BuildConfig(params (string Key, string Value)[] pairs)
    {
        var dict = pairs.ToDictionary(p => p.Key, p => (string?)p.Value);
        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }
}
