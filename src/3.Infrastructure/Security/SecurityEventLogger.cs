// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;
using Microsoft.Extensions.Logging;

namespace JOIN.Infrastructure.Security;

/// <summary>
/// Default <see cref="ISecurityEventLogger"/> implementation. Builds a <see cref="SecurityEventLog"/>
/// row and delegates persistence to <see cref="ISecurityEventRepository"/>.
/// </summary>
/// <remarks>
/// Failures are swallowed and logged via <see cref="ILogger{TCategoryName}"/>: auditing must never
/// break business flow.
/// </remarks>
public sealed class SecurityEventLogger(
    ISecurityEventRepository repository,
    ILogger<SecurityEventLogger> logger) : ISecurityEventLogger
{
    private readonly ISecurityEventRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly ILogger<SecurityEventLogger> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task LogAsync(
        SecurityEventType eventType,
        SecurityEventResult result,
        Guid? userId,
        string? ipAddress,
        string? userAgent,
        string? metadataJson = null,
        CancellationToken ct = default)
    {
        var entry = new SecurityEventLog
        {
            UserId = userId,
            EventType = (int)eventType,
            OccurredAtUtc = DateTime.UtcNow,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Result = (int)result,
            MetadataJson = metadataJson
        };

        try
        {
            await _repository.InsertAsync(entry, ct);
        }
        catch (Exception ex)
        {
            // Defensive: audit failure must never break the calling business flow.
            _logger.LogWarning(ex,
                "Failed to persist security event {EventType} for user {UserId}.",
                eventType,
                userId);
        }
    }
}
