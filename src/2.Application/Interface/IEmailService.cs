namespace JOIN.Application.Interface;

/// <summary>
/// Defines the contract for the email sending service.
/// Acts as the Adapter boundary between the Application layer and any concrete
/// email provider implementation (e.g., SendGrid, SMTP, Amazon SES).
/// This interface must remain provider-agnostic; no third-party SDK types
/// should appear in its signature or in any class that references it from
/// the Application layer.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an HTML email message to a single recipient.
    /// </summary>
    /// <param name="to">
    /// The recipient's email address (e.g., "user@example.com").
    /// Must be a valid, non-empty email address.
    /// </param>
    /// <param name="subject">
    /// The subject line of the email. Must not be null or empty.
    /// </param>
    /// <param name="htmlContent">
    /// The full HTML body of the email. Can include inline styles and standard HTML tags.
    /// </param>
    /// <returns>
    /// <c>true</c> if the message was accepted and delivered to the provider without errors;
    /// <c>false</c> if the delivery attempt failed (provider error, invalid credentials, etc.).
    /// </returns>
    Task<bool> SendEmailAsync(string to, string subject, string htmlContent);
}
