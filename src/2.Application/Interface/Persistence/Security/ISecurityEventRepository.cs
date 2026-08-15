using JOIN.Domain.Security;

namespace JOIN.Application.Interface.Persistence.Security;

/// <summary>
/// Dapper-backed repository for the <c>Security.SecurityEventLogs</c> audit table.
/// Append-only: only inserts and paginated reads are exposed.
/// </summary>
public interface ISecurityEventRepository
{
    /// <summary>
    /// Inserts an event row. Returns the number of rows affected (1 on success).
    /// </summary>
    Task<int> InsertAsync(SecurityEventLog entry, CancellationToken ct);

    /// <summary>
    /// Returns events for the supplied user, ordered by <c>OccurredAtUtc DESC</c>.
    /// Paginated; the caller is responsible for clamping page numbers and sizes.
    /// </summary>
    Task<IReadOnlyList<SecurityEventLog>> ListByUserPagedAsync(Guid userId, int pageNumber, int pageSize, CancellationToken ct);
}
