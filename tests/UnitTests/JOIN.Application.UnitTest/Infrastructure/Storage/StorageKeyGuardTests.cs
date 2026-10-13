using FluentAssertions;
using JOIN.Infrastructure.Storage;

namespace JOIN.Application.UnitTest.Infrastructure.Storage;

/// <summary>
/// Unit tests for <see cref="StorageKeyGuard"/>. La guarda es el primer
/// filtro común a los cuatro adapters de storage: una clave maliciosa
/// no debe llegar a tocar el filesystem ni el servicio de nube.
/// </summary>
public sealed class StorageKeyGuardTests
{
    /// <summary>
    /// Verifies that empty / whitespace-only keys are rejected before
    /// any segment parsing — the early return.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureSafeKey_WhenKeyIsBlank_ShouldThrow(string key)
    {
        var act = () => StorageKeyGuard.EnsureSafeKey(key);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*must not be empty*");
    }

    /// <summary>
    /// Verifies that <c>..</c> path-traversal segments are rejected
    /// (both separators, both with and without a sibling segment).
    /// </summary>
    [Theory]
    [InlineData("..")]
    [InlineData("../escape.txt")]
    [InlineData("company/../../../etc/escape.txt")]
    [InlineData("..\\windows-escape.txt")]
    [InlineData("company\\..\\..\\escape.txt")]
    public void EnsureSafeKey_WhenKeyAttemptsPathTraversal_ShouldThrow(string key)
    {
        var act = () => StorageKeyGuard.EnsureSafeKey(key);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*resolves outside the configured root*");
    }

    /// <summary>
    /// Verifies that absolute paths are rejected (rooted on either
    /// platform). The check uses <see cref="System.IO.Path.IsPathRooted"/>
    /// which recognizes both <c>/</c> (Unix) and drive letters / UNC
    /// roots (Windows).
    /// </summary>
    [Theory]
    [InlineData("/etc/escape.txt")]
    [InlineData("\\windows\\escape.txt")]
    public void EnsureSafeKey_WhenKeyIsAbsolute_ShouldThrow(string key)
    {
        var act = () => StorageKeyGuard.EnsureSafeKey(key);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*resolves outside the configured root*");
    }

    /// <summary>
    /// Verifies that a legitimate nested key returns its segments,
    /// normalized by both separators.
    /// </summary>
    [Fact]
    public void EnsureSafeKey_WhenKeyIsLegitimate_ShouldReturnSegments()
    {
        var segments = StorageKeyGuard.EnsureSafeKey("company/ticket-uuid/file.pdf");

        segments.Should().Equal("company", "ticket-uuid", "file.pdf");
    }

    [Fact]
    public void EnsureSafeKey_WhenKeyUsesBackslashSeparators_ShouldReturnSegments()
    {
        var segments = StorageKeyGuard.EnsureSafeKey("company\\ticket-uuid\\file.pdf");

        segments.Should().Equal("company", "ticket-uuid", "file.pdf");
    }
}
