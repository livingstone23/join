using System.Net.Http;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace JOIN.Infrastructure.Messaging.SendGrid;

/// <summary>
/// Adapter that bridges the <see cref="IEmailService"/> contract with the SendGrid API.
/// Encapsulates all SendGrid SDK details so that the Application layer remains
/// completely provider-agnostic (Adapter Pattern — Architecture Pillar 4).
/// </summary>
/// <remarks>
/// Registered as a Typed Client via <c>AddHttpClient&lt;IEmailService, SendGridEmailAdapter&gt;()</c>
/// so the framework-provided <see cref="HttpClient"/> is the one used to reach SendGrid.
/// This is required by the resilience pipeline configured in
/// <c>DependencyInjection.AddInfrastructureServices</c> (Polly v8 standard handler).
/// </remarks>
public sealed class SendGridEmailAdapter(
    HttpClient httpClient,
    IOptions<SendGridOptions> options,
    ILogger<SendGridEmailAdapter> logger) : IEmailService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly SendGridOptions _options = options.Value;

    /// <inheritdoc />
    /// <summary>
    /// Builds an HTML email via SendGrid's <see cref="MailHelper"/> and submits it
    /// through the official <see cref="SendGridClient"/>. Returns <c>true</c> when
    /// the API responds with a 2xx status code; otherwise logs the failure and returns
    /// <c>false</c> without propagating an exception to the caller.
    /// </summary>
    public async Task<bool> SendEmailAsync(string to, string subject, string htmlContent)
    {
        // -----------------------------------------------------------------
        // 1. Guard: validate required configuration at runtime
        // -----------------------------------------------------------------
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            logger.LogError(
                "[SendGridEmailAdapter] SendGrid ApiKey is not configured. " +
                "Set the 'SendGrid:ApiKey' value in appsettings or environment variables.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_options.FromEmail))
        {
            logger.LogError(
                "[SendGridEmailAdapter] SendGrid FromEmail is not configured. " +
                "Set the 'SendGrid:FromEmail' value in appsettings or environment variables.");
            return false;
        }

        // -----------------------------------------------------------------
        // 2. Guard: validate caller-supplied parameters
        // -----------------------------------------------------------------
        if (string.IsNullOrWhiteSpace(to))
        {
            logger.LogWarning("[SendGridEmailAdapter] SendEmailAsync called with an empty 'to' address.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            logger.LogWarning("[SendGridEmailAdapter] SendEmailAsync called with an empty 'subject'.");
            return false;
        }

        // -----------------------------------------------------------------
        // 3. Build the SendGrid message
        // -----------------------------------------------------------------
        try
        {
            var from = new EmailAddress(_options.FromEmail, _options.FromName);
            var recipient = new EmailAddress(to);

            SendGridMessage message = MailHelper.CreateSingleEmail(
                from,
                recipient,
                subject,
                plainTextContent: null,
                htmlContent: htmlContent);

            // -----------------------------------------------------------------
            // 4. Send via the official SendGrid client, reusing the injected
            //    HttpClient so the resilience pipeline (retry / circuit-breaker)
            //    wired in DependencyInjection wraps the actual HTTP traffic.
            // -----------------------------------------------------------------
            var client = new SendGridClient(_httpClient, _options.ApiKey);
            Response response = await client.SendEmailAsync(message);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "[SendGridEmailAdapter] Email sent successfully to {Recipient} | Subject: {Subject} | Status: {StatusCode}",
                    to, subject, (int)response.StatusCode);
                return true;
            }

            // Non-2xx: log the status and body for diagnostics
            string body = await response.Body.ReadAsStringAsync();
            logger.LogWarning(
                "[SendGridEmailAdapter] SendGrid rejected the request. " +
                "Recipient: {Recipient} | Status: {StatusCode} | Body: {Body}",
                to, (int)response.StatusCode, body);

            return false;
        }
        catch (Exception ex)
        {
            // Controlled failure: never let an email fault crash the caller
            logger.LogError(
                ex,
                "[SendGridEmailAdapter] Unexpected error while sending email to {Recipient} | Subject: {Subject}",
                to, subject);

            return false;
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// SPEC 72 — sends the full message: copies, Reply-To and attachments. The message is built by hand
    /// (not with <see cref="MailHelper"/>) because the helper only covers the single-recipient case.
    /// The <c>X-Message-Id</c> header of the answer is the identifier kept in the history of the email.
    /// </summary>
    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            logger.LogError(
                "[SendGridEmailAdapter] SendGrid ApiKey is not configured. " +
                "Set the 'SendGrid:ApiKey' value in appsettings or environment variables.");
            return EmailSendResult.Fail("The email provider is not configured.");
        }

        if (string.IsNullOrWhiteSpace(message.To))
        {
            logger.LogWarning("[SendGridEmailAdapter] SendAsync called with an empty 'to' address.");
            return EmailSendResult.Fail("The recipient address is empty.");
        }

        if (string.IsNullOrWhiteSpace(message.Subject))
        {
            logger.LogWarning("[SendGridEmailAdapter] SendAsync called with an empty 'subject'.");
            return EmailSendResult.Fail("The subject is empty.");
        }

        var fromEmail = string.IsNullOrWhiteSpace(message.FromEmail) ? _options.FromEmail : message.FromEmail;
        var fromName = string.IsNullOrWhiteSpace(message.FromName) ? _options.FromName : message.FromName;

        if (string.IsNullOrWhiteSpace(fromEmail))
        {
            logger.LogError(
                "[SendGridEmailAdapter] Neither the company nor the global sender is configured. " +
                "Set 'SendGrid:FromEmail' or the sender of the company settings.");
            return EmailSendResult.Fail("The sender address is not configured.");
        }

        try
        {
            var sendGridMessage = new SendGridMessage
            {
                From = new EmailAddress(fromEmail, fromName),
                Subject = message.Subject,
                HtmlContent = message.HtmlBody
            };

            sendGridMessage.AddTo(new EmailAddress(message.To));

            foreach (var cc in message.Cc.Where(address => !string.IsNullOrWhiteSpace(address)))
            {
                sendGridMessage.AddCc(new EmailAddress(cc));
            }

            if (!string.IsNullOrWhiteSpace(message.ReplyTo))
            {
                sendGridMessage.SetReplyTo(new EmailAddress(message.ReplyTo));
            }

            foreach (var attachment in message.Attachments)
            {
                sendGridMessage.AddAttachment(
                    attachment.FileName,
                    Convert.ToBase64String(attachment.Content),
                    attachment.ContentType,
                    disposition: "attachment");
            }

            var client = new SendGridClient(_httpClient, _options.ApiKey);
            Response response = await client.SendEmailAsync(sendGridMessage, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var providerMessageId = ReadMessageId(response);
                logger.LogInformation(
                    "[SendGridEmailAdapter] Email sent successfully to {Recipient} | Subject: {Subject} | Status: {StatusCode} | MessageId: {MessageId}",
                    message.To, message.Subject, (int)response.StatusCode, providerMessageId);
                return EmailSendResult.Ok(providerMessageId);
            }

            var body = await response.Body.ReadAsStringAsync();
            logger.LogWarning(
                "[SendGridEmailAdapter] SendGrid rejected the request. " +
                "Recipient: {Recipient} | Status: {StatusCode} | Body: {Body}",
                message.To, (int)response.StatusCode, body);

            return EmailSendResult.Fail($"SendGrid answered {(int)response.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "[SendGridEmailAdapter] Unexpected error while sending email to {Recipient} | Subject: {Subject}",
                message.To, message.Subject);

            return EmailSendResult.Fail(ex.Message);
        }
    }

    /// <summary>The <c>X-Message-Id</c> header of the answer, when the provider sends it.</summary>
    private static string? ReadMessageId(Response response)
    {
        if (response.Headers.TryGetValues("X-Message-Id", out var values))
        {
            return values.FirstOrDefault();
        }

        return null;
    }
}
