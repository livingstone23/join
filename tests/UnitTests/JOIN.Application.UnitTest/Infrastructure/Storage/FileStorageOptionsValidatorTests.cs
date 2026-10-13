using FluentAssertions;
using JOIN.Domain.Enums;
using JOIN.Infrastructure.Storage;

namespace JOIN.Application.UnitTest.Infrastructure.Storage;

/// <summary>
/// Unit tests for <see cref="FileStorageOptionsValidator"/>. La
/// validación se enchufa a <c>ValidateOnStart</c> en el arranque de
/// la API; estos tests cubren cada branch de
/// <see cref="FileStorageOptions.Provider"/> para garantizar que un
/// provider mal configurado hace fallar el arranque y que el mensaje
/// nombra la configuración faltante.
/// </summary>
public sealed class FileStorageOptionsValidatorTests
{
    private readonly FileStorageOptionsValidator _validator = new();

    [Fact]
    public void Validate_WhenLocalProviderWithDefaults_ShouldSucceed()
    {
        // Sin sección FileStorage:Local la API usa el RootPath por defecto.
        var result = _validator.Validate(null, new FileStorageOptions());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenLocalProviderWithBlankRootPath_ShouldNameTheKey()
    {
        var options = new FileStorageOptions { Local = new LocalStorageOptions { RootPath = " " } };

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("FileStorage:Local:RootPath");
    }

    [Fact]
    public void Validate_WhenAzureBlobProviderAndSectionComplete_ShouldSucceed()
    {
        var options = new FileStorageOptions
        {
            Provider = StorageProviderKind.AzureBlob,
            AzureBlob = new AzureBlobStorageOptions
            {
                ConnectionString = "DefaultEndpointsProtocol=https;AccountName=x;AccountKey=y;",
                ContainerName = "ticket-attachments"
            }
        };

        _validator.Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenAzureBlobProviderMissingContainerName_ShouldNameTheKey()
    {
        // Falla rápido: la API no debe arrancar si Provider=AzureBlob
        // pero no hay ContainerName. Un error de despliegue se ve al
        // levantar, no en la primera subida.
        var options = new FileStorageOptions
        {
            Provider = StorageProviderKind.AzureBlob,
            AzureBlob = new AzureBlobStorageOptions
            {
                ConnectionString = "DefaultEndpointsProtocol=https;AccountName=x;AccountKey=y;"
            }
        };

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("FileStorage:AzureBlob:ContainerName")
            .And.NotContain("FileStorage:AzureBlob:ConnectionString");
    }

    [Fact]
    public void Validate_WhenS3ProviderAndSectionComplete_ShouldSucceed()
    {
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

        _validator.Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenS3ProviderWithEmptySection_ShouldNameEveryMissingKey()
    {
        var options = new FileStorageOptions { Provider = StorageProviderKind.S3 };

        var missing = FileStorageOptionsValidator.GetMissingKeys(options);

        missing.Should().BeEquivalentTo(
            "FileStorage:S3:BucketName",
            "FileStorage:S3:Region",
            "FileStorage:S3:AccessKeyId",
            "FileStorage:S3:SecretAccessKey");
    }

    [Fact]
    public void Validate_WhenCloudflareR2ProviderMissingAccountId_ShouldNameTheKey()
    {
        var options = new FileStorageOptions
        {
            Provider = StorageProviderKind.CloudflareR2,
            CloudflareR2 = new CloudflareR2StorageOptions
            {
                BucketName = "join-attachments",
                AccessKeyId = "ak",
                SecretAccessKey = "sk"
            }
        };

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("FileStorage:CloudflareR2:AccountId");
    }

    [Fact]
    public void Validate_WhenCloudflareR2ProviderAndSectionComplete_ShouldSucceed()
    {
        var options = new FileStorageOptions
        {
            Provider = StorageProviderKind.CloudflareR2,
            CloudflareR2 = new CloudflareR2StorageOptions
            {
                AccountId = "acc",
                BucketName = "join-attachments",
                AccessKeyId = "ak",
                SecretAccessKey = "sk"
            }
        };

        _validator.Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenProviderIsUnknown_ShouldFail()
    {
        var options = new FileStorageOptions { Provider = (StorageProviderKind)99 };

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("FileStorage:Provider");
    }
}
