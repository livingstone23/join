using System.Collections.Generic;

namespace JOIN.Infrastructure.HealthChecks;

/// <summary>
/// Represents options for health check email alerting.
/// </summary>
public sealed class HealthCheckAlertOptions
{
    /// <summary>
    /// Gets or sets the list of administrator recipients for emergency notifications.
    /// </summary>
    public List<string> AdminEmails { get; set; } = [];

    /// <summary>
    /// Gets or sets the email subject prefix for health check notifications.
    /// </summary>
    public string SubjectPrefix { get; set; } = "[JOIN CRM] Health Alert";
}
