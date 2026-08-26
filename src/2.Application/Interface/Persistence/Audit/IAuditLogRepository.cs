using JOIN.Domain.Audit;

namespace JOIN.Application.Interface.Persistence.Audit;

/// <summary>
/// Dapper-backed repository for the append-only <c>Security.AuditLogs</c> bitácora.
/// All inserts bypass EF Core's change tracker so they stay cheap when invoked
/// from handlers that already own a unit-of-work write.
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>
    /// Inserts a single bitácora row. Returns the number of rows affected (1 on success).
    /// </summary>
    Task<int> InsertAsync(AuditLog entry, CancellationToken ct = default);

    /// <summary>
    /// Inserts multiple bitácora rows in a single batch. Used by bulk handlers
    /// (e.g. the RoleSystemOptions matrix upsert) to avoid N round-trips.
    /// </summary>
    Task<int> InsertManyAsync(IEnumerable<AuditLog> entries, CancellationToken ct = default);

    /// <summary>
    /// Returns a page of bitácora rows, the total count, and a map of resolved actor names.
    /// <paramref name="companyId"/> = null means "all tenants" (SuperAdmin only);
    /// any non-null value scopes the query to that tenant.
    /// Order is fixed at <c>ChangedAtUtc DESC, Id DESC</c>.
    /// Pagination branches between SQL Server (<c>OFFSET … FETCH NEXT</c>) and
    /// PostgreSQL (<c>LIMIT/OFFSET</c>) based on the configured provider.
    /// <para>
    /// The names dictionary is keyed by <c>ChangedBy</c> and carries the resolved
    /// <c>FirstName + ' ' + LastName</c> from <c>Security.Users</c>. Keys with no
    /// matching user (deleted accounts, the literal <c>"System"</c>) are absent.
    /// </para>
    /// </summary>
    Task<(IReadOnlyList<AuditLog> Items, IReadOnlyDictionary<string, string> ChangedByNames, int TotalCount)> ListPagedAsync(
        Guid? companyId,
        string? entityName,
        Guid? entityId,
        string? changedBy,
        string? action,
        DateTime? fromUtc,
        DateTime? toUtcExclusive,
        int pageNumber,
        int pageSize,
        CancellationToken ct = default);
}
