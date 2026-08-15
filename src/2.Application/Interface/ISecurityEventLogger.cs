using JOIN.Domain.Security;

namespace JOIN.Application.Interface;

/// <summary>
/// Domain service that records a single security event into <c>Security.SecurityEventLogs</c>
/// via the application-layer Dapper repository.
/// Implementations are scoped per request and never throw — auditing failure must not break business flow.
/// </summary>
public interface ISecurityEventLogger
{
    /// <summary>
    /// Persists a security event for the supplied caller.
    /// </summary>
    /// <param name="eventType">Catalogue value from <see cref="SecurityEventType"/>.</param>
    /// <param name="result">Outcome classification from <see cref="SecurityEventResult"/>.</param>
    /// <param name="userId">Affected user. Nullable so the row survives user deletion.</param>
    /// <param name="ipAddress">Optional origin IP (X-Forwarded-For first hop, then RemoteIpAddress).</param>
    /// <param name="userAgent">Optional origin User-Agent header.</param>
    /// <param name="metadataJson">Optional JSON context payload.</param>
    /// <param name="ct">Cancellation token forwarded to the underlying Dapper call.</param>
    Task LogAsync(
        SecurityEventType eventType,
        SecurityEventResult result,
        Guid? userId,
        string? ipAddress,
        string? userAgent,
        string? metadataJson = null,
        CancellationToken ct = default);
}
