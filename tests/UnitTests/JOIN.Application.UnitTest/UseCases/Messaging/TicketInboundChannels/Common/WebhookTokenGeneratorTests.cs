using System.Text.RegularExpressions;
using FluentAssertions;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Common;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketInboundChannels.Common;

/// <summary>
/// SPEC 47 F9 — unit tests for <see cref="WebhookTokenGenerator"/>.
/// Covers the Base64Url-output and unpredictability guarantees from the spec.
/// </summary>
public sealed class WebhookTokenGeneratorTests
{
    /// <summary>
    /// The base64url alphabet plus length floor documented in the spec.
    /// 32 random bytes → 43 Base64Url characters (with padding stripped).
    /// </summary>
    private static readonly Regex Base64UrlRegex = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    /// <summary>
    /// Each call must yield a non-empty token whose alphabet matches Base64Url
    /// (no '+', '/', or '=') and whose length is at least 43 characters,
    /// matching the 32 random bytes of entropy stated by SPEC 47 F2.
    /// </summary>
    [Fact]
    public void Generate_ShouldReturnBase64UrlTokenWithAtLeast43Characters()
    {
        var token = WebhookTokenGenerator.Generate();

        token.Should().NotBeNullOrEmpty();
        token.Length.Should().BeGreaterThanOrEqualTo(43);
        Base64UrlRegex.IsMatch(token).Should().BeTrue();
    }

    /// <summary>
    /// Two consecutive calls must return different tokens — otherwise a leaked
    /// token would be worthless and a flake in <see cref="RandomNumberGenerator"/>
    /// would silently degrade security.
    /// </summary>
    [Fact]
    public void Generate_ConsecutiveCalls_ShouldReturnDifferentValues()
    {
        var first = WebhookTokenGenerator.Generate();
        var second = WebhookTokenGenerator.Generate();

        first.Should().NotBe(second);
    }
}
