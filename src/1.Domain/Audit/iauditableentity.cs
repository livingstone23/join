


namespace JOIN.Domain.Audit;



/// <summary>
/// Defines the contract for entities that require traceability and auditing.
/// Essential for classes like ApplicationUser that cannot inherit from BaseAuditableEntity 
/// due to C#'s single-class inheritance limitation.
/// </summary>
public interface IAuditableEntity
{
    /// <summary>
    /// Gets or sets the UTC date and time when the entity was created.
    /// </summary>
    DateTime Created { get; set; }

    /// <summary>
    /// Gets or sets the user identifier who created this record.
    /// </summary>
    string? CreatedBy { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time of the last modification.
    /// </summary>
    DateTime? LastModified { get; set; }

    /// <summary>
    /// Gets or sets the user identifier who last modified this record.
    /// </summary>
    string? LastModifiedBy { get; set; }

    /// <summary>
    /// Property used for soft deletion. A value of 0 indicates an active record,
    /// while a value of 1 indicates a deleted record.
    /// </summary>
    int GcRecord { get; set; }
    
}