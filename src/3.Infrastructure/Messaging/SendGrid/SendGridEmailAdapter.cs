using System.Net.Http;
using JOIN.Application.Interface;
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
}
