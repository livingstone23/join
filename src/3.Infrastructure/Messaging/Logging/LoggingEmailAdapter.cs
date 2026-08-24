// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Interface;
using Microsoft.Extensions.Logging;

namespace JOIN.Infrastructure.Messaging.Logging;

/// <summary>
/// Development-only <see cref="IEmailService"/> that writes the payload to the application
/// log instead of contacting an external provider. Returns <c>true</c> so the invite /
/// forgot-password / setup-password flows do not roll back the EF unit-of-work when
/// SendGrid (or any provider) runs out of credits or is unreachable.
/// Activate via <c>"Email": { "Provider": "Log" }</c> in <c>appsettings.Development.json</c>;
/// the switch in <c>DependencyInjection.AddInfrastructureServices</c> keeps production on
/// SendGrid because the Development overrides file is not loaded outside Development.
/// </summary>
/// <remarks>
/// Side note on PII: the body preview logs the reset-password token inside the setup link.
/// That is acceptable in Development but if this adapter is ever wired into a shared
/// environment, redact the token before logging.
/// </remarks>
public sealed class LoggingEmailAdapter(ILogger<LoggingEmailAdapter> logger) : IEmailService
{
    private const int BodyPreviewMaxLength = 512;

    /// <inheritdoc />
    public Task<bool> SendEmailAsync(string to, string subject, string htmlContent)
    {
        if (string.IsNullOrWhiteSpace(to))
        {
            logger.LogWarning("[LoggingEmailAdapter] SendEmailAsync invoked with empty 'to' address.");
            return Task.FromResult(false);
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            logger.LogWarning("[LoggingEmailAdapter] SendEmailAsync invoked with empty 'subject'.");
            return Task.FromResult(false);
        }

        var bodyPreview = htmlContent is { Length: > BodyPreviewMaxLength }
            ? htmlContent[..BodyPreviewMaxLength] + "..."
            : htmlContent;

        logger.LogInformation(
            "[LoggingEmailAdapter] Email → {Recipient} | Subject: {Subject} | Body (first {Chars} chars): {Body}",
            to, subject, BodyPreviewMaxLength, bodyPreview);

        // Always succeed: dev-mode must not roll back the unit of work over a missing
        // outbound channel. Production uses SendGridEmailAdapter which reports real failures.
        return Task.FromResult(true);
    }
}
