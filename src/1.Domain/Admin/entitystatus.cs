using JOIN.Domain.Audit;



namespace JOIN.Domain.Admin;



/// <summary>
/// Catalog that defines the possible operational states for administrative entities.
/// Examples: Active, Paused, Closed, Concluded.
/// </summary>
public class EntityStatus : BaseAuditableEntity
{
    /// <summary>
    /// Gets or sets the unique name of the status (e.g., "Active").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a detailed description of what this status represents in the workflow.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Internal code to facilitate logic without depending on the Guid Oid.
    /// Example: 1 for Active, 2 for Paused.
    /// </summary>
    public int Code { get; set; }

    // --- Navigation Properties ---
    
    public virtual ICollection<Project> Projects { get; set; } = new List<Project>();
    public virtual ICollection<Area> Areas { get; set; } = new List<Area>();
}