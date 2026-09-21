using JOIN.Domain.Security;

namespace JOIN.Application.Common;

/// <summary>
/// Derives the active 2FA methods for a user directly from <see cref="ApplicationUser.IsMfaEnabled"/>
/// / <see cref="ApplicationUser.IsEmailOtpEnabled"/> — the single source of truth reused by the login
/// challenge issuer and the challenge send/verify handlers so a mid-challenge method change (enable
/// or disable) never leaves a stale method list in play.
/// </summary>
public static class MfaMethodResolver
{
    /// <summary>TOTP method identifier used across DTOs and persisted fields.</summary>
    public const string Totp = "totp";

    /// <summary>Email OTP method identifier used across DTOs and persisted fields.</summary>
    public const string Email = "email";

    /// <summary>
    /// Returns the 2FA methods currently active for <paramref name="user"/>, in a stable order
    /// (<c>totp</c> before <c>email</c>).
    /// </summary>
    public static IReadOnlyCollection<string> ResolveAvailable(ApplicationUser user)
    {
        var methods = new List<string>(2);

        if (user.IsMfaEnabled)
        {
            methods.Add(Totp);
        }

        if (user.IsEmailOtpEnabled)
        {
            methods.Add(Email);
        }

        return methods;
    }
}
