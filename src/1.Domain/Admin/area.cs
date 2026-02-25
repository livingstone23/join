


using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;



namespace JOIN.Domain.Admin;



/// <summary>
/// Represents a functional department within a Company.
/// Status is managed through the EntityStatus catalog for dynamic workflow support.
/// </summary>
public class Area : BaseAuditableEntity
{
    
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Foreign key to the owner Company (Tenant).
    /// </summary>
    public Guid CompanyId { get; set; }

    /// <summary>
    /// Foreign key to the dynamic EntityStatus catalog.
    /// </summary>
    public Guid EntityStatusId { get; set; }

    // --- Navigation Properties ---

    public virtual Company Company { get; set; } = null!;
    public virtual EntityStatus Status { get; set; } = null!;
    public virtual ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();

}