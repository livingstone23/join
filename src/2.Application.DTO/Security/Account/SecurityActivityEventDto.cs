namespace JOIN.Application.DTO.Security.Account;

/// <summary>
/// One row in the security-activity feed. Maps to a <c>Security.SecurityEventLogs</c>
/// row and exposes the event/result fields as their string name (LoginSucceeded, etc.)
/// so the API surface stays readable without leaking .NET enum integer values.
/// </summary>
public sealed record SecurityActivityEventDto
{
    /// <summary>
    /// UTC instant the event was observed.
    /// </summary>
    public DateTime OccurredAtUtc { get; init; }

    /// <summary>
    /// Event name (string version of <see cref="SecurityEventType"/>).
    /// </summary>
    public string Event { get; init; } = string.Empty;

    /// <summary>
    /// Outcome (string version of <see cref="SecurityEventResult"/>).
    /// </summary>
    public string Result { get; init; } = string.Empty;

    /// <summary>
    /// Originating IP (X-Forwarded-For first hop, then RemoteIpAddress).
    /// </summary>
    public string? IpAddress { get; init; }

    /// <summary>
    /// Originating User-Agent header (device descriptor).
    /// </summary>
    public string? Device { get; init; }
}
