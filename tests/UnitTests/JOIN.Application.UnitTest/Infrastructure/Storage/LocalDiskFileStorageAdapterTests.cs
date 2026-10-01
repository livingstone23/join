using System.Text;
using FluentAssertions;
using JOIN.Infrastructure.Storage.Local;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UnitTest.Infrastructure.Storage;

/// <summary>
/// Unit tests for <see cref="LocalDiskFileStorageAdapter"/>. These tests
/// touch the real filesystem (under a unique temp directory) — no mocks,
/// because the value of testing a filesystem adapter is precisely to
/// verify the bytes-on-disk behavior, not the call contract.
/// </summary>
public sealed class LocalDiskFileStorageAdapterTests : IDisposable
{
    private readonly string _rootPath;
    private readonly LocalDiskFileStorageAdapter _adapter;

    public LocalDiskFileStorageAdapterTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "join-fs-tests", Guid.NewGuid().ToString("N"));
        var options = Options.Create(new FileStorageOptions { RootPath = _rootPath });
        _adapter = new LocalDiskFileStorageAdapter(options, NullLogger<LocalDiskFileStorageAdapter>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that <c>SaveAsync</c> writes the content to disk and
    /// reports the observed size in bytes — the handler persists this as
    /// <c>TicketDocument.SizeBytes</c> and uses it to validate
    /// <c>MaxFileSizeBytes</c> on subsequent reads.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WhenStorageKeyIsValid_ShouldWriteFileAndReturnSize()
    {
        // Arrange
        var storageKey = "companyA/ticketX/report.pdf";
        var fullPath = Path.Combine(_rootPath, storageKey);
        var content = "hello, world"u8.ToArray();

        // Act
        var result = await _adapter.SaveAsync(new MemoryStream(content), storageKey, "text/plain", CancellationToken.None);

        // Assert
        result.StorageKey.Should().Be(storageKey);
        result.SizeBytes.Should().Be(content.Length);
        File.Exists(fullPath).Should().BeTrue();
        (await File.ReadAllBytesAsync(fullPath)).Should().Equal(content);
    }

    /// <summary>
    /// Verifies that <c>SaveAsync</c> creates parent directories on demand —
    /// a storage key like <c>company/ticket-uuid/file.pdf</c> implies two
    /// nested directories that may not exist yet at the moment of upload.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WhenParentDirectoriesDoNotExist_ShouldCreateThem()
    {
        // Arrange
        var storageKey = $"deep/nested/{Guid.NewGuid():N}/data.bin";
        var content = new byte[] { 0x01, 0x02, 0x03 };

        // Act
        var result = await _adapter.SaveAsync(new MemoryStream(content), storageKey, "application/octet-stream", CancellationToken.None);

        // Assert
        result.SizeBytes.Should().Be(3);
        File.Exists(Path.Combine(_rootPath, storageKey)).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that <c>OpenReadAsync</c> returns the bytes that
    /// <c>SaveAsync</c> previously wrote — the round-trip is the
    /// guarantee that the controller can serve a faithful download.
    /// </summary>
    [Fact]
    public async Task OpenReadAsync_WhenFileExists_ShouldReturnWrittenContent()
    {
        // Arrange
        var storageKey = "companyA/round-trip/notes.txt";
        var payload = Encoding.UTF8.GetBytes("ticket evidence payload");
        await _adapter.SaveAsync(new MemoryStream(payload), storageKey, "text/plain", CancellationToken.None);

        // Act
        var stream = await _adapter.OpenReadAsync(storageKey, CancellationToken.None);

        // Assert
        stream.Should().NotBeNull();
        await using (stream)
        {
            using var reader = new StreamReader(stream);
            (await reader.ReadToEndAsync()).Should().Be("ticket evidence payload");
        }
    }

    /// <summary>
    /// Verifies that <c>OpenReadAsync</c> returns <c>null</c> when the
    /// requested key does not resolve to a file on disk — the handler
    /// translates this to <c>FILE_NOT_FOUND_IN_STORAGE</c> (404), not a
    /// generic 500, so inconsistent metadata/storage state is surfaced
    /// cleanly to ops.
    /// </summary>
    [Fact]
    public async Task OpenReadAsync_WhenStorageKeyIsMissing_ShouldReturnNull()
    {
        // Act
        var stream = await _adapter.OpenReadAsync("companyA/never-uploaded/x.bin", CancellationToken.None);

        // Assert
        stream.Should().BeNull();
    }

    /// <summary>
    /// Verifies that <c>DeleteAsync</c> removes the file and reports
    /// <c>true</c> when the file existed, <c>false</c> when it did not.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeleteAsync_ShouldReturnExistenceFlag(bool preCreate)
    {
        // Arrange
        var storageKey = "companyA/delete-me/old.bin";
        var fullPath = Path.Combine(_rootPath, storageKey);
        if (preCreate)
        {
            await _adapter.SaveAsync(new MemoryStream(new byte[] { 0x00 }), storageKey, "application/octet-stream", CancellationToken.None);
            File.Exists(fullPath).Should().BeTrue();
        }

        // Act
        var result = await _adapter.DeleteAsync(storageKey, CancellationToken.None);

        // Assert
        result.Should().Be(preCreate);
        File.Exists(fullPath).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a <c>storageKey</c> with <c>..</c> segments (path
    /// traversal attempt) is rejected with <see cref="InvalidOperationException"/>
    /// before any filesystem call is made — this is the security guard
    /// the adapter exists to provide. The test asserts the file is NOT
    /// created at the escape target, which would be the worst-case bug
    /// (silent write outside the configured root).
    /// </summary>
    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("companyA/../../../etc/escape.txt")]
    [InlineData("..\\windows-escape.txt")]
    [InlineData("companyA\\..\\..\\escape.txt")]
    [InlineData("/etc/escape.txt")]
    public async Task SaveAsync_WhenStorageKeyAttemptsPathTraversal_ShouldThrowAndNotWrite(string maliciousKey)
    {
        // Act
        var act = async () => await _adapter.SaveAsync(
            new MemoryStream(new byte[] { 0xFF }),
            maliciousKey,
            "application/octet-stream",
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*resolves outside the configured root*");

        // The escape target must not exist anywhere on the real filesystem
        // outside the configured root. We check the temp area below the
        // configured root only — proving the file did not get written there.
        var expectedUnderRoot = Path.GetFullPath(Path.Combine(_rootPath, maliciousKey));
        File.Exists(expectedUnderRoot).Should().BeFalse();
    }

    /// <summary>
    /// Verifies the root check compares against "root + separator": a key that
    /// resolves into a sibling directory sharing the root's name as a prefix
    /// (e.g. "<root>-evil") must be rejected, not accepted by a bare StartsWith.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WhenKeyTargetsSiblingDirectoryWithSamePrefix_ShouldThrow()
    {
        var siblingKey = $"../{Path.GetFileName(_rootPath)}-evil/escape.txt";

        var act = async () => await _adapter.SaveAsync(
            new MemoryStream(new byte[] { 0x01 }), siblingKey, "text/plain", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*resolves outside the configured root*");
        Directory.Exists(_rootPath + "-evil").Should().BeFalse();
    }

    /// <summary>
    /// Verifies that backslash separators are normalized on every platform,
    /// so a legitimate nested key written with "\" lands in nested folders.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WhenKeyUsesBackslashSeparators_ShouldWriteNestedFile()
    {
        var result = await _adapter.SaveAsync(
            new MemoryStream(new byte[] { 0x01, 0x02 }), "companyA\\ticketB\\file.txt", "text/plain", CancellationToken.None);

        result.SizeBytes.Should().Be(2);
        File.Exists(Path.Combine(_rootPath, "companyA", "ticketB", "file.txt")).Should().BeTrue();
    }
}
