namespace JOIN.Domain.Audit;

/// <summary>
/// Entities whose mutations are recorded in <c>Security.AuditLogs</c>.
/// Persisted as the enum name (string), not the underlying int.
/// </summary>
public enum AuditedEntity
{
    Role = 1,
    User = 2,
    RoleSystemOption = 3,
    UserRoleCompany = 4,
    UserCompany = 5,
    RoleCompany = 6
}
