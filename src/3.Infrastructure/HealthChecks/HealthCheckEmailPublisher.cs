using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JOIN.Application.Interface;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JOIN.Infrastructure.HealthChecks;

/// <summary>
/// Publishes health check notifications by email for emergency scenarios.
/// </summary>
public sealed class HealthCheckEmailPublisher(
    IEmailService emailService,
    IOptions<HealthCheckAlertOptions> options,
    ILogger<HealthCheckEmailPublisher> logger) : IHealthCheckPublisher
{
    private readonly IEmailService _emailService = emailService;
    private readonly HealthCheckAlertOptions _options = options.Value;
    private readonly ILogger<HealthCheckEmailPublisher> _logger = logger;
    private int _lastPublishedStatus = (int)HealthStatus.Healthy;

    /// <summary>
    /// Publishes a notification when the system becomes unhealthy or when the global status changes.
    /// </summary>
    /// <param name="report">The aggregated health report.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        var currentStatus = report.Status;
        var previousStatus = (HealthStatus)Interlocked.Exchange(ref _lastPublishedStatus, (int)currentStatus);
        var statusChanged = previousStatus != currentStatus;

        if (currentStatus != HealthStatus.Unhealthy && !statusChanged)
        {
            return;
        }

        if (_options.AdminEmails.Count == 0)
        {
            _logger.LogWarning("Health alert skipped because no admin recipients are configured.");
            return;
        }

        var subject = $"{_options.SubjectPrefix} - {currentStatus}";
        var htmlBody = BuildHtmlBody(report, previousStatus, statusChanged);

        foreach (var recipient in _options.AdminEmails)
        {
            if (string.IsNullOrWhiteSpace(recipient))
            {
                continue;
            }

            var sent = await _emailService.SendEmailAsync(
                recipient.Trim(),
                subject,
                htmlBody);

            if (!sent)
            {
                _logger.LogWarning("Health alert email could not be delivered to {Recipient}.", recipient);
            }
        }
    }

    private static string BuildHtmlBody(HealthReport report, HealthStatus previousStatus, bool statusChanged)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<h2>JOIN CRM Health Status Alert</h2>");
        sb.AppendLine($"<p><strong>Current Status:</strong> {report.Status}</p>");
        sb.AppendLine($"<p><strong>Previous Status:</strong> {previousStatus}</p>");
        sb.AppendLine($"<p><strong>Status Changed:</strong> {(statusChanged ? "Yes" : "No")}</p>");
        sb.AppendLine($"<p><strong>Timestamp (UTC):</strong> {DateTimeOffset.UtcNow:O}</p>");
        sb.AppendLine($"<p><strong>Total Duration:</strong> {report.TotalDuration}</p>");

        sb.AppendLine("<table border='1' cellpadding='8' cellspacing='0' style='border-collapse:collapse;'>");
        sb.AppendLine("<thead><tr><th>Check</th><th>Status</th><th>Description</th><th>Duration</th></tr></thead>");
        sb.AppendLine("<tbody>");

        foreach (var entry in report.Entries)
        {
            var description = string.IsNullOrWhiteSpace(entry.Value.Description)
                ? "N/A"
                : WebUtility.HtmlEncode(entry.Value.Description);

            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{WebUtility.HtmlEncode(entry.Key)}</td>");
            sb.AppendLine($"<td>{entry.Value.Status}</td>");
            sb.AppendLine($"<td>{description}</td>");
            sb.AppendLine($"<td>{entry.Value.Duration}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</tbody></table>");
        return sb.ToString();
    }
}
