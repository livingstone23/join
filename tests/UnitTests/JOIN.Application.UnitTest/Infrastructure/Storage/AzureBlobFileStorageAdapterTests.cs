using Azure;
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
    public void Constructor_WithContainerFactory_ShouldUseItForOperations()
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

        act.Should().ThrowAsync<InvalidOperationException>().Result
            .And.Message.Should().Be("factory invoked");
        factoryInvoked.Should().BeTrue();
    }

    private static FileStorageOptions BuildOptions() => new()
    {
        AzureBlob = new AzureBlobStorageOptions
        {
            ConnectionString = "DefaultEndpointsProtocol=https;AccountName=x;AccountKey=y;",
            ContainerName = "ticket-attachments"
        }
    };
}
