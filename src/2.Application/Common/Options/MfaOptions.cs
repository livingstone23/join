namespace JOIN.Application.Common.Options;

/// <summary>
/// Configuration options for the MFA login challenge (SPEC 32).
/// Bind from the <c>Mfa</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class MfaOptions
{
    /// <summary>Configuration section name. Must match appsettings.</summary>
    public const string SectionName = "Mfa";

    /// <summary>
    /// Minutes a <c>challengeToken</c> lives before expiring. Default 5.
    /// </summary>
    public int ChallengeExpirationMinutes { get; init; } = 5;
}
