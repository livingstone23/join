namespace JOIN.Domain.Audit;

/// <summary>
/// Action recorded for an audited entity mutation.
/// Persisted as the enum name (string) for human readability in SQL.
/// </summary>
public enum AuditAction
{
    Created = 1,
    Updated = 2,
    Deleted = 3
}
