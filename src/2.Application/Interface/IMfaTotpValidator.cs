namespace JOIN.Application.Interface;

/// <summary>
/// Validates Time-based One-Time Password codes and produces provisioning URIs for QR codes
/// (RFC 6238). Wraps the Otp.NET library so the application layer does not depend on
/// third-party crypto directly.
/// </summary>
public interface IMfaTotpValidator
{
    /// <summary>
    /// Generates a new 32-character Base32 secret suitable for TOTP enrollment.
    /// </summary>
    /// <returns>Base32-encoded secret without padding.</returns>
    string GenerateSecret();

    /// <summary>
    /// Builds an <c>otpauth://totp/...</c> provisioning URI used by authenticator apps.
    /// </summary>
    /// <param name="secret">Base32 secret produced by <see cref="GenerateSecret"/>.</param>
    /// <param name="accountName">User identifier embedded in the URI (typically the email).</param>
    /// <param name="issuer">Display name shown in the authenticator app ("JOIN").</param>
    string BuildQrCodeUri(string secret, string accountName, string issuer);

    /// <summary>
    /// Validates the supplied 6-digit TOTP code against the supplied secret using a
    /// window of <paramref name="windowSteps"/> (default ±30 s).
    /// </summary>
    /// <param name="secret">Base32 secret the user enrolled with.</param>
    /// <param name="code">User-supplied code. Trimmed of whitespace before evaluation.</param>
    /// <param name="windowSteps">Number of 30-second steps accepted before/after now.</param>
    bool ValidateCode(string secret, string code, int windowSteps = 1);
}
