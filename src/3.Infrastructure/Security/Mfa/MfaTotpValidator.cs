// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Interface;
using OtpNet;

namespace JOIN.Infrastructure.Security.Mfa;

/// <summary>
/// Otp.NET-backed implementation of <see cref="IMfaTotpValidator"/>. Secrets are 20-byte
/// Base32 (160-bit, RFC 6238 recommendation); the TOTP step is the Otp.NET default of 30 s.
/// </summary>
public sealed class MfaTotpValidator : IMfaTotpValidator
{
    private const int SecretByteLength = 20;

    /// <inheritdoc />
    public string GenerateSecret()
    {
        var key = KeyGeneration.GenerateRandomKey(SecretByteLength);
        return Base32Encoding.ToString(key).TrimEnd('=');
    }

    /// <inheritdoc />
    public string BuildQrCodeUri(string secret, string accountName, string issuer)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("Secret must be supplied.", nameof(secret));
        }
        if (string.IsNullOrWhiteSpace(accountName))
        {
            throw new ArgumentException("Account name must be supplied.", nameof(accountName));
        }

        // otpauth URI is the well-known specification; OtpNet.OtpUri is fragile across
        // versions, so we hand-roll the canonical string and URL-encode the parts.
        var label = Uri.EscapeDataString($"{issuer}:{accountName}");
        var encodedIssuer = Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{label}?secret={Uri.EscapeDataString(secret)}&issuer={encodedIssuer}";
    }

    /// <inheritdoc />
    public bool ValidateCode(string secret, string code, int windowSteps = 1)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var totp = new Totp(Base32Encoding.ToBytes(secret));
        var window = new VerificationWindow(windowSteps, windowSteps);
        var trimmed = code.Trim();
        return totp.VerifyTotp(trimmed, out _, window);
    }
}
