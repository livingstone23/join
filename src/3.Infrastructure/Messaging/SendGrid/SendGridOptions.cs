namespace JOIN.Infrastructure.Messaging.SendGrid;

/// <summary>
/// Strongly-typed configuration options for the SendGrid email provider.
/// Bind this class to the "SendGrid" section in appsettings.json via
/// <c>services.Configure&lt;SendGridOptions&gt;(configuration.GetSection("SendGrid"))</c>.
/// </summary>
public sealed class SendGridOptions
{
    /// <summary>
    /// The SendGrid API key used to authenticate requests.
    /// Store this value in a secret manager or environment variable in production;
    /// never commit a real key to source control.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// The verified sender email address shown in the "From" field.
    /// Must be a domain or single sender already verified in your SendGrid account.
    /// </summary>
    public string FromEmail { get; init; } = string.Empty;

    /// <summary>
    /// The display name shown alongside the "From" email address
    /// (e.g., "JOIN CRM Notifications").
    /// </summary>
    public string FromName { get; init; } = string.Empty;
}
