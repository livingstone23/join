# Blueprint: Domain Entity (DDD & Multi-Tenancy)
This blueprint defines the standard for creating domain entities within JOIN.Domain. Entities must be pure business representations and adhere to specific inheritance rules for auditing and data isolation.

### 1 Inheritance Options
- BaseTenantEntity: Used for data belonging to a specific company, such as Customers, Projects, or Branches. It includes CompanyId and is automatically processed by the Tenant Filter.
- BaseAuditableEntity: Used for global or shared data requiring traceability, such as Countries or Currencies. It includes audit fields (Created, LastModified) but does not include CompanyId.
- BaseEntity: Used for simple master or system tables that do not require extended auditing.

### 2 Coding Rules
- *Constructors: Primary Constructors are FORBIDDEN in domain entities. EF Core requires a parameterless constructor (which can be protected) for materialization and proxy creation.
- Properties: Use public virtual for navigation properties to allow for Lazy Loading when necessary.
- Collections: Always initialize collections in the constructor or as auto-implemented properties to avoid NullReferenceException.


## 3. Standard Entity Example

```csharp
using JOIN.Domain.Common;

namespace JOIN.Domain.Admin;

/// <summary>
/// Represents a Customer within a specific company context.
/// </summary>
public class Customer : BaseTenantEntity
{
    /// <summary>
    /// Gets or sets the first name of the customer.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the associated identification type.
    /// </summary>
    public Guid IdentificationTypeId { get; set; }

    // Navigation Properties
    public virtual IdentificationType IdentificationType { get; set; } = null!;
    public virtual ICollection<CustomerAddress> Addresses { get; set; } = [];

    /// <summary>
    /// Standard constructor for EF Core materialization.
    /// </summary>
    public Customer() 
    { 
        Addresses = new HashSet<CustomerAddress>();
    }
}
´´

4. Review Checklist
[ ] Does it inherit from the correct base class (BaseTenantEntity or BaseAuditableEntity)?
[ ] Does it avoid the use of Primary Constructors?
[ ] Does it include English XML comments for every property?
[ ] Are navigation properties marked as virtual
[ ] Are all collections properly initialized?