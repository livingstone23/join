using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FluentAssertions;
using JOIN.Domain.Enums;
using JOIN.Infrastructure.Storage;
using JOIN.Infrastructure.Storage.AzureBlob;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.Infrastructure.Storage.AzureBlob;

/// <summary>
/// Unit tests for <see cref="AzureBlobFileStorageAdapter"/>. SPEC 49-post:
/// los tests de los adapters de nube mockean el cliente del SDK; los
/// smoke contra el servicio real viven en F7.5 y solo se corren con
/// credenciales provistas por el usuario.
/// </summary>
/// <remarks>
/// El SDK de Azure v12 no expone una jerarquía fácil de mockear para
/// todos los métodos que el adapter usa (las sobrecargas "con opciones"
/// viven en <c>BlockBlobClient</c>, no en <c>BlobClient</c>). Por eso
/// estos tests cubren solo:
/// <list type="bullet">
///   <item><c>Kind</c> reporta <c>AzureBlob</c>.</item>
///   <item>La guarda de path traversal rechaza la clave antes de tocar
///   el servicio (mismo criterio que Local y S3).</item>
///   <item>El adaptador arma la <c>BlobContainerClient</c> a partir de
///   la cadena de conexión + container name (verificable con un setup
///   que lance antes de que la API real intervenga).</item>
/// </list>
/// El contrato de upload/download contra el servicio real se cubre con
/// los smoke de F7.5.
/// </remarks>
public sealed class AzureBlobFileStorageAdapterTests
{
    [Fact]
    public void Kind_ShouldReportAzureBlob()
    {
        var options = BuildOptions();
        var logger = NullLogger<AzureBlobFileStorageAdapter>.Instance;

        var adapter = new AzureBlobFileStorageAdapter(Options.Create(options), logger);

        adapter.Kind.Should().Be(StorageProviderKind.AzureBlob);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../escape.txt")]
    [InlineData("companyA/../../../etc/escape.txt")]
    [InlineData("..\\windows-escape.txt")]
    [InlineData("/etc/escape.txt")]
    public async Task SaveAsync_WhenStorageKeyAttemptsPathTraversal_ShouldThrow(string maliciousKey)
    {
        var options = BuildOptions();
        var logger = NullLogger<AzureBlobFileStorageAdapter>.Instance;

        var adapter = new AzureBlobFileStorageAdapter(Options.Create(options), logger);

        var act = async () => await adapter.SaveAsync(
            new MemoryStream(new byte[] { 0x01 }),
            maliciousKey,
            "application/octet-stream",
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*resolves outside the configured root*");
    }

    [Fact]
    public async Task SaveAsync_WhenStorageKeyIsBlank_ShouldThrow()
    {
        var options = BuildOptions();
        var logger = NullLogger<AzureBlobFileStorageAdapter>.Instance;

        var adapter = new AzureBlobFileStorageAdapter(Options.Create(options), logger);

        var act = async () => await adapter.SaveAsync(
            new MemoryStream(new byte[] { 0x01 }),
            "",
            "application/octet-stream",
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*must not be empty*");
    }

    [Fact]
    public async Task Constructor_WithContainerFactory_ShouldUseItForOperations()
    {
        // Verifica que el constructor de testing recibe el factory y
        // lo invoca. Una exception del factory se propaga al primer
        // uso; eso basta para probar que el wiring es correcto.
        var options = BuildOptions();
        var logger = NullLogger<AzureBlobFileStorageAdapter>.Instance;
        var factoryInvoked = false;

        var adapter = new AzureBlobFileStorageAdapter(
            Options.Create(options),
            logger,
            () =>
            {
                factoryInvoked = true;
                throw new InvalidOperationException("factory invoked");
            });

        var act = async () => await adapter.SaveAsync(
            new MemoryStream(new byte[] { 0x01 }),
            "companyA/x.bin",
            "application/octet-stream",
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("factory invoked");
        factoryInvoked.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveAsync_ShouldUploadWithKeyAndContentType_AndCreateContainerOnlyWhenEnabled(bool createContainer)
    {
        var options = BuildOptions();
        options.AzureBlob.CreateContainerIfNotExists = createContainer;
        var (container, blob) = BuildContainer("companyA/ticketX/report.pdf");
        blob.Setup(b => b.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());
        var adapter = BuildAdapter(options, container);
        var payload = new byte[] { 0x01, 0x02, 0x03 };

        var result = await adapter.SaveAsync(new MemoryStream(payload), "companyA/ticketX/report.pdf", "application/pdf", CancellationToken.None);

        result.StorageKey.Should().Be("companyA/ticketX/report.pdf");
        result.SizeBytes.Should().Be(payload.Length);
        blob.Verify(b => b.UploadAsync(
            It.IsAny<Stream>(),
            It.Is<BlobUploadOptions>(o => o.HttpHeaders.ContentType == "application/pdf"),
            It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(c => c.CreateIfNotExistsAsync(
            It.IsAny<PublicAccessType>(),
            It.IsAny<IDictionary<string, string>>(),
            It.IsAny<BlobContainerEncryptionScopeOptions>(),
            It.IsAny<CancellationToken>()), createContainer ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task OpenReadAsync_WhenBlobExists_ShouldReturnItsContent()
    {
        var (container, blob) = BuildContainer("companyA/x.bin");
        var content = new MemoryStream([0x01]);
        blob.Setup(b => b.DownloadStreamingAsync(It.IsAny<BlobDownloadOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(
                BlobsModelFactory.BlobDownloadStreamingResult(content: content),
                Mock.Of<Response>()));
        var adapter = BuildAdapter(BuildOptions(), container);

        var opened = await adapter.OpenReadAsync("companyA/x.bin", CancellationToken.None);

        opened.Should().BeSameAs(content);
    }

    [Fact]
    public async Task OpenReadAsync_WhenBlobDoesNotExist_ShouldReturnNull()
    {
        var (container, blob) = BuildContainer("companyA/missing.bin");
        blob.Setup(b => b.DownloadStreamingAsync(It.IsAny<BlobDownloadOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "BlobNotFound"));
        var adapter = BuildAdapter(BuildOptions(), container);

        var opened = await adapter.OpenReadAsync("companyA/missing.bin", CancellationToken.None);

        opened.Should().BeNull();
    }

    [Fact]
    public async Task OpenReadAsync_WhenServiceFailsWithOtherError_ShouldPropagate()
    {
        var (container, blob) = BuildContainer("companyA/x.bin");
        blob.Setup(b => b.DownloadStreamingAsync(It.IsAny<BlobDownloadOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(403, "AuthorizationFailure"));
        var adapter = BuildAdapter(BuildOptions(), container);

        var act = async () => await adapter.OpenReadAsync("companyA/x.bin", CancellationToken.None);

        (await act.Should().ThrowAsync<RequestFailedException>()).Which.Status.Should().Be(403);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeleteAsync_ShouldReportWhetherTheBlobExisted(bool existed)
    {
        var (container, blob) = BuildContainer("companyA/x.bin");
        blob.Setup(b => b.DeleteIfExistsAsync(
                It.IsAny<DeleteSnapshotsOption>(),
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(existed, Mock.Of<Response>()));
        var adapter = BuildAdapter(BuildOptions(), container);

        var deleted = await adapter.DeleteAsync("companyA/x.bin", CancellationToken.None);

        deleted.Should().Be(existed);
    }

    private static (Mock<BlobContainerClient> Container, Mock<BlobClient> Blob) BuildContainer(string blobName)
    {
        var container = new Mock<BlobContainerClient>();
        var blob = new Mock<BlobClient>();
        container.Setup(c => c.GetBlobClient(blobName)).Returns(blob.Object);
        return (container, blob);
    }

    private static AzureBlobFileStorageAdapter BuildAdapter(FileStorageOptions options, Mock<BlobContainerClient> container) =>
        new(Options.Create(options), NullLogger<AzureBlobFileStorageAdapter>.Instance, () => container.Object);

    private static FileStorageOptions BuildOptions() => new()
    {
        AzureBlob = new AzureBlobStorageOptions
        {
            ConnectionString = "DefaultEndpointsProtocol=https;AccountName=x;AccountKey=y;",
            ContainerName = "ticket-attachments"
        }
    };
}
