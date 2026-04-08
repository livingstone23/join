using JOIN.Domain.Audit;



namespace JOIN.Domain.Admin;



/// <summary>
/// Catalog that defines the possible operational states for administrative entities.
/// Examples: Active, Pausada, Bloqueado, PendienteIniciar.
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
    /// Example: 1 for Active, 2 for Pausada.
    /// </summary>
    public int Code { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the status allows the entity to continue operating.
    /// </summary>
    public bool IsOperative { get; set; } = true;

    // --- Navigation Properties ---
    
    public virtual ICollection<Project> Projects { get; set; } = new List<Project>();
    public virtual ICollection<Area> Areas { get; set; } = new List<Area>();
    
}