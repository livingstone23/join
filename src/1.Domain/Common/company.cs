using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;



namespace JOIN.Domain.Common;



/// <summary>
/// Represents the central organization or Tenant in the system.
/// All business data (Customers, Tickets, Projects) is logically partitioned by this entity
/// to ensure strict data isolation and multi-tenancy support.
/// </summary>
public class Company : BaseAuditableEntity
{
    /// <summary>
    /// Gets or sets the official legal name of the company.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a brief description of the company's business purpose or industry.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the company's Tax Identification Number (RUC, NIT, CIF, etc.).
    /// Used for legal and billing identification.
    /// </summary>
    public string TaxId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the official business email address for corporate notifications.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets the primary contact phone number.
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Gets or sets the official website URL of the company.
    /// </summary>
    public string? WebSite { get; set; }

    /// <summary>
    /// Indicates if the company is currently active in the system.
    /// Inactive companies may restrict login access for their users.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // --- Navigation Properties ---

    /// <summary>
    /// Collection of customers belonging to this company.
    /// </summary>
    public virtual ICollection<Customer> Customers { get; set; } = new List<Customer>();

    /// <summary>
    /// Collection of internal areas or departments defined within the company.
    /// </summary>
    public virtual ICollection<Area> Areas { get; set; } = new List<Area>();

    /// <summary>
    /// Collection of projects managed under this company's umbrella.
    /// </summary>
    public virtual ICollection<Project> Projects { get; set; } = new List<Project>();


    /// <summary>
    /// Collection of tickets under this company's humbrella
    /// </summary>
    public virtual ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();


    /// <summary>
    /// Collection of UserRoles under this company's humbrella
    /// </summary>
    public virtual ICollection<UserRoleCompany> UserRoleCompanies { get; set; } = new List<UserRoleCompany>();

    /// <summary>
    /// Collection of users that have been granted access to this company.
    /// </summary>
    public virtual ICollection<UserCompany> UserCompanies { get; set; } = new List<UserCompany>();
    
}

