using System.Net;
using System.Text;

namespace JOIN.Application.Common.Email;

/// <summary>
/// Static HTML builders for the three transactional emails introduced by SPEC 27:
/// invitation, forgot-password, and forced reset. HTML is plain — no template engine —
/// and uses <see cref="WebUtility.HtmlEncode"/> on every interpolated user-controlled
/// string and <see cref="WebUtility.UrlEncode"/> on every query parameter.
/// </summary>
public static class AuthEmailTemplates
{
    private const string BrandName = "JOIN CRM";

    // ──────────────────────────────────────────────
    //  Path helpers
    // ──────────────────────────────────────────────

    private const string SetupPasswordPath = "/setup-password";
    private const string ResetPasswordPath = "/reset-password";

    // ──────────────────────────────────────────────
    //  Public builders
    // ──────────────────────────────────────────────

    /// <summary>
    /// Builds the invitation HTML sent after an admin invites a new user via
    /// <c>POST /Users/invite</c>. The link points to the setup-password route so the
    /// new user can set their first password.
    /// </summary>
    public static string BuildInvitation(string firstName, string companyName, string link)
    {
        var safeFirst = WebUtility.HtmlEncode(firstName);
        var safeCompany = WebUtility.HtmlEncode(companyName);
        var safeLink = WebUtility.HtmlEncode(link);
        return $$"""
            <p>Hi {{safeFirst}},</p>
            <p>You have been invited to join <strong>{{safeCompany}}</strong> on {{BrandName}}.</p>
            <p>To activate your account and set your password, click the link below:</p>
            <p><a href="{{safeLink}}">{{safeLink}}</a></p>
            <p>If you did not expect this invitation you can safely ignore this email.</p>
            """;
    }

    /// <summary>
    /// Builds the forgot-password HTML. Generic message: does not reveal whether the
    /// email exists in the system.
    /// </summary>
    public static string BuildForgotPassword(string firstName, string link)
    {
        var safeFirst = WebUtility.HtmlEncode(firstName);
        var safeLink = WebUtility.HtmlEncode(link);
        return $$"""
            <p>Hi {{safeFirst}},</p>
            <p>We received a request to reset the password for your {{BrandName}} account.</p>
            <p>If you made this request, click the link below to choose a new password:</p>
            <p><a href="{{safeLink}}">{{safeLink}}</a></p>
            <p>If you did not request a password reset you can safely ignore this email.</p>
            """;
    }

    /// <summary>
    /// Builds the forced-reset HTML sent by an admin via
    /// <c>POST /Users/{userId}/force-password-reset</c>. Includes the admin-supplied
    /// reason so the user understands the context.
    /// </summary>
    public static string BuildForcedReset(string firstName, string link, string reason)
    {
        var safeFirst = WebUtility.HtmlEncode(firstName);
        var safeReason = WebUtility.HtmlEncode(reason);
        var safeLink = WebUtility.HtmlEncode(link);
        return $$"""
            <p>Hi {{safeFirst}},</p>
            <p>An administrator has triggered a password reset on your {{BrandName}} account.</p>
            <p><strong>Reason:</strong> {{safeReason}}</p>
            <p>To choose a new password, click the link below:</p>
            <p><a href="{{safeLink}}">{{safeLink}}</a></p>
            """;
    }

    // ──────────────────────────────────────────────
    //  Link builder (shared)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Builds an absolute <c>{baseUrl}{path}?email=…&amp;token=…</c> link, trimming a
    /// trailing slash from <paramref name="baseUrl"/> and url-encoding the parameters.
    /// </summary>
    public static string BuildLink(string baseUrl, string path, string email, string token)
    {
        var trimmedBase = baseUrl.TrimEnd('/');
        var encodedEmail = WebUtility.UrlEncode(email);
        var encodedToken = WebUtility.UrlEncode(token);
        var sb = new StringBuilder();
        sb.Append(trimmedBase).Append(path).Append("?email=").Append(encodedEmail).Append("&token=").Append(encodedToken);
        return sb.ToString();
    }

    /// <summary>Returns the front route for first-password setup (used by invite + setup handlers).</summary>
    public static string SetupPasswordPathName => SetupPasswordPath;

    /// <summary>Returns the front route for a normal reset (used by forgot-password).</summary>
    public static string ResetPasswordPathName => ResetPasswordPath;
}
