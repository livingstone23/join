using JOIN.Domain.Security;

namespace JOIN.Application.Interface.Persistence.Security;

/// <summary>
/// Persistence contract for the MFA recovery-code set stored under <c>Security.UserMfaRecoveryCodes</c>.
/// Dapper-backed so we can avoid EF change tracker overhead during MFA setup rotation.
/// </summary>
public interface IUserMfaRecoveryCodeRepository
{
    /// <summary>
    /// Bulk-inserts the supplied recovery codes. Returns the number of rows written.
    /// </summary>
    Task<int> InsertManyAsync(IEnumerable<UserMfaRecoveryCode> codes, CancellationToken ct);

    /// <summary>
    /// Returns every unused (<c>UsedAtUtc IS NULL</c>) recovery code for the user. Used by
    /// DisableMfaCommand's recovery-code path.
    /// </summary>
    Task<IReadOnlyList<UserMfaRecoveryCode>> ListUnusedByUserAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Marks a single recovery code as used (<c>UsedAtUtc = @utcNow</c>). Returns <c>false</c>
    /// when the row no longer exists or was already consumed.
    /// </summary>
    Task<bool> MarkUsedAsync(Guid codeId, DateTime utcNow, CancellationToken ct);

    /// <summary>
    /// Soft-deletes every recovery code row for the user. Called when MFA is disabled or
    /// a fresh setup is requested.
    /// </summary>
    Task<int> DeleteAllByUserAsync(Guid userId, DateTime utcNow, CancellationToken ct);
}
