namespace JOIN.Application.Common.Options;

/// <summary>
/// Frontend URL options consumed by handlers that build absolute links inside
/// transactional emails (invitation, forgot-password, forced reset).
/// Bind from the <c>AppUrls</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class AppUrlsOptions
{
    /// <summary>Configuration section name. Must match appsettings.</summary>
    public const string SectionName = "AppUrls";

    /// <summary>
    /// Absolute base URL of the Angular front, without trailing slash.
    /// Example: <c>https://app.join.com</c>.
    /// Handlers return <c>FRONTEND_URL_NOT_CONFIGURED</c> when this value is blank.
    /// </summary>
    public string FrontendBaseUrl { get; set; } = string.Empty;
}
