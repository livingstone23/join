namespace JOIN.Domain.Security;

/// <summary>
/// Outcome classification for entries in <c>Security.SecurityEventLogs</c>.
/// Stored as <c>int</c> in the <c>Result</c> column.
/// </summary>
public enum SecurityEventResult
{
    Success = 1,
    Failure = 2,
    Denied = 3,
}
