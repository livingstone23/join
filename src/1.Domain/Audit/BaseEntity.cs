


namespace JOIN.Domain.Audit;



/// <summary>
/// Represents the base abstract class for all Domain Entities.
/// An entity is defined by its unique identity (Id) rather than its attributes.
/// </summary>
public abstract class BaseEntity
{

    /// <summary>
    /// Unique identifier for the entity. 
    /// Using Guid (UUID) ensures global uniqueness across distributed systems 
    /// and prevents ID exhaustion or predictability issues.
    /// </summary>
    public Guid Id { get; protected set; }

    /// <summary>
    /// Initializes a new instance of the BaseEntity with a new unique Guid.
    /// </summary>
    protected BaseEntity() => Id = Guid.NewGuid();

}