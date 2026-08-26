using JOIN.Domain.Audit;

namespace JOIN.Application.Interface;

/// <summary>
/// Captures mutations of the six audited security entities (<see cref="AuditedEntity"/>).
/// The actor's identity, tenant, and IP are resolved internally from <see cref="ICurrentUserService"/> —
/// handlers never supply them.
/// <para>
/// Failures of the bitácora are swallowed inside the implementation: a missing or broken
/// <c>Security.AuditLogs</c> table must never block the business operation that already committed.
/// </para>
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Records a single mutation. <c>oldValues</c> and <c>newValues</c> should be the flat
    /// column values (not navigation properties); sensitive keys are discarded by the implementation.
    /// </summary>
    Task LogAsync(
        AuditedEntity entity,
        Guid entityId,
        AuditAction action,
        string? entityLabel = null,
        IReadOnlyDictionary<string, object?>? oldValues = null,
        IReadOnlyDictionary<string, object?>? newValues = null,
        string? metadataJson = null,
        CancellationToken ct = default);

    /// <summary>
    /// Records a batch of mutations in a single round-trip. Used by bulk handlers
    /// (e.g. the RoleSystemOptions matrix upsert) where one request may touch
    /// hundreds of rows.
    /// </summary>
    Task LogManyAsync(
        IEnumerable<AuditLogEntryRequest> entries,
        CancellationToken ct = default);
}

/// <summary>
/// Per-row payload for <see cref="IAuditLogger.LogManyAsync"/>.
/// </summary>
public sealed record AuditLogEntryRequest(
    AuditedEntity Entity,
    Guid EntityId,
    AuditAction Action,
    string? EntityLabel = null,
    IReadOnlyDictionary<string, object?>? OldValues = null,
    IReadOnlyDictionary<string, object?>? NewValues = null,
    string? MetadataJson = null);
