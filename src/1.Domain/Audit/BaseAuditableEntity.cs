


namespace JOIN.Domain.Audit;



/// <summary>
/// Extends BaseEntity to include traceability properties.
/// Essential for monitoring who performed actions and when, 
/// supporting security audits and data integrity checks.
/// </summary>
public abstract class BaseAuditableEntity : BaseEntity
{
    /// <summary>
    /// Gets or sets the UTC date and time when the entity was created.
    /// </summary>
    public DateTime Created { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the user identifier who created this record.
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time of the last modification.
    /// </summary>
    public DateTime? LastModified { get; set; }

    /// <summary>
    /// Gets or sets the user identifier who last modified this record.
    /// </summary>
    public string? LastModifiedBy { get; set; }

    /// <summary>
    /// Property used for soft deletion. A value of 0 indicates an active record, 
    /// while a value of 1 indicates a deleted record.
    /// </summary>
    /// <value></value>
    public int GcRecord { get; set; } = 0;
    
}