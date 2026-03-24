


using JOIN.Domain.Common;

namespace JOIN.Domain.Audit;



/// <summary>
/// Base class for all tenant-specific entities, ensuring they include audit properties and a reference to the owning company.
/// </summary> <summary>
/// This class serves as a foundation for entities that are specific to a 
/// tenant, providing a CompanyId property to link the entity to its owning company.  
/// </summary>
public abstract class BaseTenantEntity : BaseAuditableEntity
{

    /// <summary>
    /// Gets or sets the identifier of the company (tenant) that owns this entity.
    /// </summary>
    /// <value></value>
    public Guid CompanyId { get; set; }



    // --- Navigation Properties ---

    /// <summary>
    /// Navigation property to the owning Company (Tenant).
    /// This allows Entity Framework Core to automatically map the CompanyId foreign key.
    /// </summary>
    public virtual Company Company { get; set; } = null!;
    
}
