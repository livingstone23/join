namespace JOIN.Application.Interface;

using JOIN.Application.Interface.Email;



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

    /// <summary>
    /// SPEC 72 — sends a full message (copies, Reply-To and attachments) and reports what the
    /// provider answered, instead of a bare boolean. <see cref="SendEmailAsync"/> is not touched:
    /// its fifteen callers (authentication, alerts, tickets) keep working as they are.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">Token to cancel the delivery.</param>
    /// <returns>
    /// <see cref="EmailSendResult.Ok"/> when the provider accepted the message (with its identifier
    /// when it gives one), <see cref="EmailSendResult.Fail"/> with the reason otherwise.
    /// </returns>
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);

}
