


using System.Reflection;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using JOIN.Domain.Support;
using JOIN.Infrastructure.Contexts;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;



namespace JOIN.Infrastructure.Contexts;



/// <summary>
/// Represents the core database context for the JOIN application.
/// Integrates ASP.NET Core Identity for user management and role-based access control (RBAC),
/// specifically tailored for a multi-tenant architecture.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    private readonly AuditableEntitySaveChangesInterceptor _auditableEntitySaveChangesInterceptor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplicationDbContext"/> class.
    /// </summary>
    /// <param name="options">The options to be used by a <see cref="DbContext"/>.</param>
    /// <param name="auditableEntitySaveChangesInterceptor">
    /// The interceptor responsible for automatically updating audit fields (Created/Modified) on save.
    /// </param>
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        AuditableEntitySaveChangesInterceptor auditableEntitySaveChangesInterceptor)
        : base(options)
    {
        _auditableEntitySaveChangesInterceptor = auditableEntitySaveChangesInterceptor;
    }

    // ========================================================================
    // SECURITY & MULTI-TENANCY DBSETS
    // ========================================================================

    /// <summary>
    /// Gets or sets the set of User-Role-Company relationships.
    /// Defines which specific roles a user possesses within a given tenant (Company).
    /// </summary>
    public DbSet<UserRoleCompany> UserRoleCompanies => Set<UserRoleCompany>();

    /// <summary>
    /// Gets or sets the set of User-Company relationships.
    /// Defines the tenants (Companies) a user has been granted access to.
    /// </summary>
    public DbSet<UserCompany> UserCompanies => Set<UserCompany>();


    // ========================================================================
    // BUSINESS DOMAIN DBSETS
    // ========================================================================
    
    // --- Catálogos Geográficos ---
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<Province> Provinces => Set<Province>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<Municipality> Municipalities => Set<Municipality>();
    public DbSet<StreetType> StreetTypes => Set<StreetType>();

    // --- Administración & Clientes ---
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<CustomerContact> CustomerContacts => Set<CustomerContact>();
    public DbSet<IdentificationType> IdentificationTypes => Set<IdentificationType>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<EntityStatus> EntityStatuses => Set<EntityStatus>();


    // --- Omnicanalidad & Tickets (Soporte) ---
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketStatus> TicketStatuses => Set<TicketStatus>();
    public DbSet<TicketComplexity> TicketComplexities => Set<TicketComplexity>();
    public DbSet<TimeUnit> TimeUnits => Set<TimeUnit>();
    public DbSet<TicketNotification> TicketNotifications => Set<TicketNotification>();
    public DbSet<CommunicationChannel> CommunicationChannels => Set<CommunicationChannel>();
    public DbSet<UserCommunicationChannel> UserCommunicationChannels => Set<UserCommunicationChannel>();


    // ========================================================================
    // CONFIGURATIONS & OVERRIDES
    // ========================================================================

    /// <summary>
    /// Configures the schema needed for the identity framework and applies custom entity configurations.
    /// </summary>
    /// <param name="builder">The builder being used to construct the model for this context.</param>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // CRITICAL: base.OnModelCreating(builder) MUST be called first.
        // This ensures that Identity framework configures the base tables 
        // (AspNetUsers, AspNetRoles, AspNetUserRoles, etc.) before any custom configurations are applied.
        base.OnModelCreating(builder);

        // Dynamically load all Fluent API configurations (IEntityTypeConfiguration<T>)
        // defined within the current assembly (e.g., UserRoleCompanyConfiguration).
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    /// <summary>
    /// Configures the database context options, registering custom interceptors and logging.
    /// </summary>
    /// <param name="optionsBuilder">A builder used to create or modify options for this context.</param>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Register the auditing interceptor to populate BaseAuditableEntity fields automatically
        optionsBuilder.AddInterceptors(_auditableEntitySaveChangesInterceptor);

        // Enable sensitive data logging for detailed error tracking 
        // NOTE: Ensure this is driven by environment variables so it is disabled in Production.
        optionsBuilder.EnableSensitiveDataLogging();

        base.OnConfiguring(optionsBuilder);
    }

    /// <summary>
    /// Saves all changes made in this context to the database asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous save operation.</returns>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // The actual audit logic is handled by the registered AuditableEntitySaveChangesInterceptor.
        return await base.SaveChangesAsync(cancellationToken);
    }
}