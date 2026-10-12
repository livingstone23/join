using FluentAssertions;
using JOIN.Services.WebApi.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace JOIN.Application.UnitTest.Services.TwilioMediaDownloader;

/// <summary>
/// SPEC 47 F9 — unit tests for <see cref="TwilioMediaDownloader"/>.
/// <para>
/// Limitation: the production class owns a <c>private static Lazy&lt;HttpClient&gt;</c>
/// — no public seam to substitute a fake <see cref="System.Net.Http.HttpMessageHandler"/>
/// from a unit test without reflection. So the "200 returns bytes" and "404
/// from the provider returns null" scenarios are best exercised by an
/// integration test that points <c>Twilio:AccountSid</c>/<c>Twilio:AuthToken</c>
/// at a fake server (e.g. <c>WireMock</c>); this unit suite covers the two
/// "no network round-trip needed" branches, which are the most likely
/// production-time failure modes (empty URL, missing credentials) and the
/// only ones a unit test can reach without coupling to the static
/// <see cref="System.Net.Http.HttpClient"/> singleton.
/// </para>
/// </summary>
public sealed class TwilioMediaDownloaderTests
{
    /// <summary>
    /// Empty <c>mediaUrl</c> short-circuits the request before any network
    /// or credential check — the contract is "return null, do not throw".
    /// </summary>
    [Fact]
    public async Task TryDownloadAsync_WhenMediaUrlIsEmpty_ShouldReturnNullWithoutThrowing()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var result = await JOIN.Services.WebApi.Services.TwilioMediaDownloader.TryDownloadAsync(
            string.Empty,
            configuration,
            NullLogger.Instance,
            CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Whitespace <c>mediaUrl</c> is treated like an empty URL.
    /// </summary>
    [Fact]
    public async Task TryDownloadAsync_WhenMediaUrlIsWhitespace_ShouldReturnNullWithoutThrowing()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var result = await JOIN.Services.WebApi.Services.TwilioMediaDownloader.TryDownloadAsync(
            "   ",
            configuration,
            NullLogger.Instance,
            CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Missing <c>Twilio:AccountSid</c>/<c>Twilio:AuthToken</c> credentials
    /// abort the download with a null return. The provider must never see
    /// an exception — the webhook is best-effort by design.
    /// </summary>
    [Fact]
    public async Task TryDownloadAsync_WhenTwilioCredentialsMissing_ShouldReturnNullWithoutThrowing()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        const string mediaUrl = "https://api.twilio.com/Media/abc";

        var result = await JOIN.Services.WebApi.Services.TwilioMediaDownloader.TryDownloadAsync(
            mediaUrl,
            configuration,
            NullLogger.Instance,
            CancellationToken.None);

        result.Should().BeNull();
    }
}
