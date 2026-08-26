// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Audit;
using JOIN.Domain.Audit;
using Microsoft.Extensions.Logging;

namespace JOIN.Infrastructure.Audit;

/// <summary>
/// Default <see cref="IAuditLogger"/> implementation. Resolves the actor (CompanyId, ChangedBy,
/// IpAddress, ChangedAtUtc) from <see cref="ICurrentUserService"/> and persists via
/// <see cref="IAuditLogRepository"/>. All repository exceptions are swallowed with a
/// warning log: a bitácora failure must never break a business operation that already committed.
/// </summary>
public sealed class AuditLogger(
    ICurrentUserService currentUserService,
    IAuditLogRepository repository,
    ILogger<AuditLogger> logger) : IAuditLogger
{
    private const string SystemActor = "System";

    private readonly ICurrentUserService _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    private readonly IAuditLogRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly ILogger<AuditLogger> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public Task LogAsync(
        AuditedEntity entity,
        Guid entityId,
        AuditAction action,
        string? entityLabel = null,
        IReadOnlyDictionary<string, object?>? oldValues = null,
        IReadOnlyDictionary<string, object?>? newValues = null,
        string? metadataJson = null,
        CancellationToken ct = default)
    {
        return LogManyAsync(
        [
            new AuditLogEntryRequest(entity, entityId, action, entityLabel, oldValues, newValues, metadataJson)
        ], ct);
    }

    /// <inheritdoc />
    public async Task LogManyAsync(IEnumerable<AuditLogEntryRequest> entries, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var rows = new List<AuditLog>();
        var now = DateTime.UtcNow;
        var companyId = _currentUserService.CompanyId;
        var changedBy = string.IsNullOrWhiteSpace(_currentUserService.UserId) ? SystemActor : _currentUserService.UserId;
        var ipAddress = _currentUserService.IpAddress;

        foreach (var entry in entries)
        {
            // Build the diff first — an Updated row whose diff is empty is not an event.
            var (oldJson, newJson) = AuditDiffBuilder.Build(entry.OldValues, entry.NewValues);

            if (entry.Action == AuditAction.Updated && oldJson is null && newJson is null)
            {
                continue;
            }

            rows.Add(new AuditLog
            {
                EntityName = entry.Entity.ToString(),
                EntityId = entry.EntityId,
                EntityLabel = entry.EntityLabel,
                Action = entry.Action.ToString(),
                CompanyId = companyId,
                ChangedBy = changedBy,
                ChangedAtUtc = now,
                IpAddress = ipAddress,
                OldValuesJson = oldJson,
                NewValuesJson = newJson,
                MetadataJson = entry.MetadataJson
            });
        }

        if (rows.Count == 0)
        {
            return;
        }

        try
        {
            await _repository.InsertManyAsync(rows, ct);
        }
        catch (Exception ex)
        {
            // Defensive: a bitácora failure must never break the calling business flow.
            // The mutation already committed; surfacing the error would mislead the operator.
            _logger.LogWarning(ex,
                "Failed to persist {Count} security audit row(s) for actor {ChangedBy} in tenant {CompanyId}.",
                rows.Count, changedBy, companyId);
        }
    }
}
