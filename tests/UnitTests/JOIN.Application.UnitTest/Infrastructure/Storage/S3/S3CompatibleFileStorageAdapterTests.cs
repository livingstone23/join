using System.Net;
using System.Text;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using JOIN.Domain.Enums;
using JOIN.Infrastructure.Storage;
using JOIN.Infrastructure.Storage.S3;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JOIN.Application.UnitTest.Infrastructure.Storage.S3;

/// <summary>
/// Unit tests for <see cref="S3CompatibleFileStorageAdapter"/>. SPEC 49-post:
/// una sola clase cubre AWS S3 y Cloudflare R2 — el <see cref="StorageProviderKind"/>
/// viaja con la fila. Los tests mockean <see cref="IAmazonS3"/>; los smoke
/// contra S3/R2 reales viven en F7.5 con credenciales provistas por el usuario.
/// </summary>
public sealed class S3CompatibleFileStorageAdapterTests
{
    [Theory]
    [InlineData(StorageProviderKind.S3)]
    [InlineData(StorageProviderKind.CloudflareR2)]
    public void Kind_ShouldEchoTheProviderItWasBuiltFor(StorageProviderKind kind)
    {
        var adapter = BuildAdapter(out _, kind: kind);

        adapter.Kind.Should().Be(kind);
    }

    [Fact]
    public async Task SaveAsync_ShouldCallPutObjectWithContentType()
    {
        var adapter = BuildAdapter(out var client, StorageProviderKind.S3, bucket: "join-bucket");
        var payload = Encoding.UTF8.GetBytes("hello, world");
        var stream = new MemoryStream(payload);

        var result = await adapter.SaveAsync(
            stream,
            "companyA/ticketX/report.pdf",
            "application/pdf",
            CancellationToken.None);

        result.StorageKey.Should().Be("companyA/ticketX/report.pdf");
        result.SizeBytes.Should().Be(payload.Length);

        client.Verify(x => x.PutObjectAsync(
            It.Is<PutObjectRequest>(r =>
                r.BucketName == "join-bucket"
                && r.Key == "companyA/ticketX/report.pdf"
                && r.ContentType == "application/pdf"
                && r.InputStream.Length == payload.Length),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_WhenContentTypeIsBlank_ShouldFallBackToApplicationOctetStream()
    {
        var adapter = BuildAdapter(out var client, StorageProviderKind.S3, bucket: "join-bucket");

        await adapter.SaveAsync(
            new MemoryStream(new byte[] { 0x01 }),
            "companyA/x.bin",
            contentType: "",
            CancellationToken.None);

        client.Verify(x => x.PutObjectAsync(
            It.Is<PutObjectRequest>(r => r.ContentType == "application/octet-stream"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_WhenStreamIsNotSeekable_ShouldStillReturnObservedSize()
    {
        // El adapter copia a un MemoryStream cuando el origen no es
        // seekable, así puede reportar el tamaño observado. El SDK
        // puede envolver el stream en su propia pipeline; aquí solo
        // nos interesa el contrato: SizeBytes == payload.Length.
        var adapter = BuildAdapter(out var client, StorageProviderKind.S3, bucket: "join-bucket");
        byte[]? uploaded = null;
        client.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((r, _) =>
            {
                // El buffer temporal debe seguir abierto mientras dura la subida.
                using var copy = new MemoryStream();
                r.InputStream.CopyTo(copy);
                uploaded = copy.ToArray();
            })
            .ReturnsAsync(new PutObjectResponse());

        var payload = Encoding.UTF8.GetBytes("hello");
        var nonSeekable = new NonSeekableReadStream(new MemoryStream(payload));

        var result = await adapter.SaveAsync(
            nonSeekable,
            "companyA/x.bin",
            "application/octet-stream",
            CancellationToken.None);

        result.SizeBytes.Should().Be(payload.Length);
        result.StorageKey.Should().Be("companyA/x.bin");
        uploaded.Should().Equal(payload);
    }

    [Theory]
    [InlineData(StorageProviderKind.S3, false)]
    [InlineData(StorageProviderKind.CloudflareR2, true)]
    public async Task SaveAsync_ShouldDisablePayloadSigningOnlyForR2(StorageProviderKind kind, bool expected)
    {
        var adapter = BuildAdapter(out var client, kind, bucket: "join-bucket");

        await adapter.SaveAsync(new MemoryStream([0x01]), "companyA/x.bin", "application/pdf", CancellationToken.None);

        client.Verify(x => x.PutObjectAsync(
            It.Is<PutObjectRequest>(r => r.DisablePayloadSigning == expected),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void BuildClientConfig_ForCloudflareR2_ShouldBuildEndpointFromAccountId()
    {
        var options = new FileStorageOptions
        {
            CloudflareR2 = new CloudflareR2StorageOptions { AccountId = "abc123" }
        };

        var config = S3CompatibleFileStorageAdapter.BuildClientConfig(options, StorageProviderKind.CloudflareR2);

        config.ServiceURL.Should().Be("https://abc123.r2.cloudflarestorage.com/");
        config.AuthenticationRegion.Should().Be("auto");
        config.RequestChecksumCalculation.Should().Be(RequestChecksumCalculation.WHEN_REQUIRED);
        config.ResponseChecksumValidation.Should().Be(ResponseChecksumValidation.WHEN_REQUIRED);
    }

    [Fact]
    public void BuildClientConfig_ForS3WithoutServiceUrl_ShouldUseTheConfiguredRegion()
    {
        var options = new FileStorageOptions { S3 = new S3StorageOptions { Region = "eu-west-1" } };

        var config = S3CompatibleFileStorageAdapter.BuildClientConfig(options, StorageProviderKind.S3);

        config.RegionEndpoint.Should().Be(RegionEndpoint.EUWest1);
        config.ForcePathStyle.Should().BeFalse();
    }

    [Fact]
    public void BuildClientConfig_ForS3WithServiceUrl_ShouldUseItAndSignWithTheRegion()
    {
        var options = new FileStorageOptions
        {
            S3 = new S3StorageOptions
            {
                Region = "us-east-1",
                ServiceUrl = "http://localhost:9000",
                ForcePathStyle = true
            }
        };

        var config = S3CompatibleFileStorageAdapter.BuildClientConfig(options, StorageProviderKind.S3);

        config.ServiceURL.Should().Be("http://localhost:9000/");
        config.AuthenticationRegion.Should().Be("us-east-1");
        config.ForcePathStyle.Should().BeTrue();
    }

    [Theory]
    [InlineData(StorageProviderKind.S3, "join-s3")]
    [InlineData(StorageProviderKind.CloudflareR2, "join-r2")]
    public void Create_ShouldPickTheBucketAndKindOfItsSection(StorageProviderKind kind, string expectedBucket)
    {
        var options = new FileStorageOptions
        {
            S3 = new S3StorageOptions { BucketName = "join-s3", Region = "us-east-1", AccessKeyId = "ak", SecretAccessKey = "sk" },
            CloudflareR2 = new CloudflareR2StorageOptions { AccountId = "acc", BucketName = "join-r2", AccessKeyId = "ak", SecretAccessKey = "sk" }
        };

        var adapter = S3CompatibleFileStorageAdapter.Create(options, kind, NullLogger.Instance);

        adapter.Kind.Should().Be(kind);
        adapter.BucketName.Should().Be(expectedBucket);
    }

    [Fact]
    public async Task OpenReadAsync_WhenObjectExists_ShouldReturnTheResponseStream()
    {
        var adapter = BuildAdapter(out var client, StorageProviderKind.S3, bucket: "join-bucket");
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("content"));
        var response = new GetObjectResponse
        {
            BucketName = "join-bucket",
            Key = "companyA/x.bin",
            ResponseStream = stream
        };
        client.Setup(x => x.GetObjectAsync("join-bucket", "companyA/x.bin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var opened = await adapter.OpenReadAsync("companyA/x.bin", CancellationToken.None);

        opened.Should().BeSameAs(stream);
    }

    [Fact]
    public async Task OpenReadAsync_WhenObjectDoesNotExist_ShouldReturnNull()
    {
        var adapter = BuildAdapter(out var client, StorageProviderKind.S3, bucket: "join-bucket");

        // S3 devuelve AmazonS3Exception con StatusCode 404 y ErrorCode
        // "NoSuchKey" cuando el objeto no existe. El constructor
        // completo varía por versión del SDK; usamos el de 5-args que
        // AWSSDK.S3 v4 expone.
        var exception = new Amazon.S3.AmazonS3Exception(
            message: "not found",
            errorType: Amazon.Runtime.ErrorType.Unknown,
            errorCode: "NoSuchKey",
            requestId: "test-request",
            statusCode: System.Net.HttpStatusCode.NotFound);
        client.Setup(x => x.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var opened = await adapter.OpenReadAsync("companyA/missing.bin", CancellationToken.None);

        opened.Should().BeNull();
    }

    [Fact]
    public async Task OpenReadAsync_WhenServiceThrowsOtherError_ShouldPropagate()
    {
        var adapter = BuildAdapter(out var client, StorageProviderKind.S3, bucket: "join-bucket");

        var exception = new Amazon.S3.AmazonS3Exception(
            message: "boom",
            errorType: Amazon.Runtime.ErrorType.Unknown,
            errorCode: "InternalError",
            requestId: "test-request",
            statusCode: System.Net.HttpStatusCode.InternalServerError);
        client.Setup(x => x.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var act = async () => await adapter.OpenReadAsync("companyA/x.bin", CancellationToken.None);

        await act.Should().ThrowAsync<Amazon.S3.AmazonS3Exception>()
            .Where(ex => ex.StatusCode == HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task DeleteAsync_ShouldCallDeleteObjectAndReportTrue()
    {
        // S3 DeleteObject no informa si el objeto existía: el adapter
        // devuelve true salvo que el SDK lance.
        var adapter = BuildAdapter(out var client, StorageProviderKind.S3, bucket: "join-bucket");
        client.Setup(x => x.DeleteObjectAsync("join-bucket", "companyA/x.bin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());

        var result = await adapter.DeleteAsync("companyA/x.bin", CancellationToken.None);

        result.Should().BeTrue();
        client.Verify(x => x.DeleteObjectAsync("join-bucket", "companyA/x.bin", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_WhenStorageKeyIsPathTraversal_ShouldThrow()
    {
        var adapter = BuildAdapter(out _, StorageProviderKind.S3, bucket: "join-bucket");

        var act = async () => await adapter.SaveAsync(
            new MemoryStream(new byte[] { 0x01 }),
            "../escape.txt",
            "application/octet-stream",
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*resolves outside the configured root*");
    }

    private static S3CompatibleFileStorageAdapter BuildAdapter(
        out Mock<IAmazonS3> client,
        StorageProviderKind kind,
        string bucket = "join-bucket")
    {
        client = new Mock<IAmazonS3>();
        return new S3CompatibleFileStorageAdapter(
            client.Object,
            bucket,
            kind,
            NullLogger<S3CompatibleFileStorageAdapter>.Instance);
    }

    /// <summary>
    /// Stream que reporta <see cref="CanSeek"/> = <c>false</c> sobre
    /// otro stream. Útil para verificar el camino de "copiar a
    /// buffer" del adapter de S3.
    /// </summary>
    private sealed class NonSeekableReadStream(Stream inner) : Stream
    {
        private long _consumed;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _consumed;
            set => throw new NotSupportedException();
        }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            _consumed += read;
            return read;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => inner.ReadAsync(buffer, offset, count, cancellationToken).ContinueWith(t =>
            {
                _consumed += t.Result;
                return t.Result;
            }, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
