using JOIN.Domain.Audit;

namespace JOIN.Domain.Audit;

/// <summary>
/// Append-only bitácora row for mutations of the six audited security entities.
/// Inherits from <see cref="BaseEntity"/> only — no <c>GcRecord</c>, no soft delete,
/// no query filter: the table grows forever and is never rewritten.
/// </summary>
public class AuditLog : BaseEntity
{
    /// <summary>
    /// Initializes a new instance with a generated identifier.
    /// </summary>
    public AuditLog() { }

    /// <summary>
    /// Initializes a new instance with the supplied identifier. Used by tests + callers
    /// that need a stable Id before persistence.
    /// </summary>
    public AuditLog(Guid id)
    {
        Id = id;
    }

    /// <summary>
    /// Enum name of the affected entity (e.g. <c>"RoleSystemOption"</c>). nvarchar(64).
    /// </summary>
    public string EntityName { get; set; } = string.Empty;

    /// <summary>
    /// Id of the affected row. Survives deletion of the underlying entity (no FK).
    /// </summary>
    public Guid EntityId { get; set; }

    /// <summary>
    /// Human-readable label of the subject at the time of the change
    /// (e.g. role name, user email, "&lt;RoleName&gt; → &lt;SystemOptionName&gt;").
    /// Frozen at write time. nvarchar(256).
    /// </summary>
    public string? EntityLabel { get; set; }

    /// <summary>
    /// "Created" | "Updated" | "Deleted". nvarchar(16).
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Tenant of the actor's token (not necessarily the affected entity's tenant).
    /// </summary>
    public Guid CompanyId { get; set; }

    /// <summary>
    /// UserId of the actor, or <c>"System"</c> for internal processes. nvarchar(64).
    /// </summary>
    public string ChangedBy { get; set; } = string.Empty;

    /// <summary>
    /// UTC instant at which the change happened.
    /// </summary>
    public DateTime ChangedAtUtc { get; set; }

    /// <summary>
    /// IP address of the actor. nvarchar(45) (fits IPv6).
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// Only the keys whose value changed. Null on Created. nvarchar(max).
    /// </summary>
    public string? OldValuesJson { get; set; }

    /// <summary>
    /// Only the keys whose value changed. Null on Deleted. nvarchar(max).
    /// </summary>
    public string? NewValuesJson { get; set; }

    /// <summary>
    /// Free-form JSON context (e.g. <c>{ "bulkOperationId": "..." }</c>, change reason). nvarchar(max).
    /// </summary>
    public string? MetadataJson { get; set; }
}
