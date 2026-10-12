using System.Security.Cryptography;

namespace JOIN.Application.UseCases.Messaging.TicketInboundChannels.Common;

/// <summary>
/// Generates <c>TicketInboundChannel.WebhookToken</c> secrets (SPEC 47 F2).
/// 32 cryptographically-secure random bytes are encoded as Base64Url without padding,
/// yielding a 43-character string. The same primitive used by the project's other
/// token issuers (refresh tokens, etc.) — no rolling of a custom helper because
/// <see cref="RandomNumberGenerator"/> is already the input source.
/// </summary>
public static class WebhookTokenGenerator
{
    private const int TokenByteLength = 32;

    /// <summary>
    /// Returns a fresh, URL-safe opaque token.
    /// </summary>
    public static string Generate()
    {
        Span<byte> buffer = stackalloc byte[TokenByteLength];
        RandomNumberGenerator.Fill(buffer);

        return Convert.ToBase64String(buffer)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
