


using System.Reflection;
using JOIN.Application.Interface;
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
/// Core database context for the JOIN CRM.
/// Inherits from IdentityDbContext to support ASP.NET Core Identity with Guid keys.
/// Enforces Multi-Tenancy (CompanyId) and Soft Delete (GcRecord) globally via Query Filters.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    private readonly AuditableEntitySaveChangesInterceptor _auditableInterceptor;
    private readonly ICurrentUserService _currentUserService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplicationDbContext"/> class.
    /// </summary>
    /// <param name="options">The options to be used by a DbContext.</param>
    /// <param name="auditableInterceptor">Interceptor to handle audit and multi-tenant fields.</param>
    /// <param name="currentUserService">Service to retrieve the current user's Tenant (CompanyId).</param>
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        AuditableEntitySaveChangesInterceptor auditableInterceptor,
        ICurrentUserService currentUserService) : base(options)
    {
        _auditableInterceptor = auditableInterceptor;
        _currentUserService = currentUserService;
    }

    // --- SECURITY MODULE (Identity already provides Users and Roles DbSets) ---
    public DbSet<UserCompany> UserCompanies { get; set; }
    public DbSet<UserRoleCompany> UserRoleCompanies { get; set; }
    public DbSet<UserConnectionLog> UserConnectionLogs { get; set; }

    // --- ADMINISTRATIVE MODULE ---
    public DbSet<Company> Companies { get; set; }
    public DbSet<SystemModule> SystemModules { get; set; }
    public DbSet<CompanyModule> CompanyModules { get; set; }
    public DbSet<SystemOption> SystemOptions { get; set; }
    public DbSet<RoleSystemOption> RoleSystemOptions { get; set; }

    // --- GLOBAL CATALOGS ---
    public DbSet<Country> Countries { get; set; }
    public DbSet<Region> Regions { get; set; }
    public DbSet<Province> Provinces { get; set; }
    public DbSet<Municipality> Municipalities { get; set; }
    public DbSet<StreetType> StreetTypes { get; set; }
    public DbSet<IdentificationType> IdentificationTypes { get; set; }

    // --- CUSTOMERS MODULE ---
    public DbSet<Customer> Customers { get; set; }
    public DbSet<CustomerAddress> CustomerAddresses { get; set; }
    public DbSet<CustomerContact> CustomerContacts { get; set; }

    // --- TICKETS MODULE (CORE) ---
    public DbSet<Ticket> Tickets { get; set; }
    public DbSet<Project> Projects { get; set; }
    public DbSet<Area> Areas { get; set; }
    public DbSet<TicketStatus> TicketStatuses { get; set; }
    public DbSet<TicketComplexity> TicketComplexities { get; set; }
    public DbSet<TimeUnit> TimeUnits { get; set; }
    public DbSet<TicketNotification> TicketNotifications { get; set; }
    public DbSet<CommunicationChannel> CommunicationChannels { get; set; }

    /// <summary>
    /// Configures the database schema, relationships, and global query filters.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // 1. CRITICAL: Call the base method to ensure ASP.NET Identity tables are configured correctly.
        base.OnModelCreating(builder);

        // 2. Apply separate Fluent API configurations (IEntityTypeConfiguration<T>) from the assembly.
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // 3. SPECIAL CONFIGURATIONS: Prevent multiple cascade paths in SQL Server.
        
        // Ticket -> CreatedByUser Relationship
        builder.Entity<Ticket>()
            .HasOne(t => t.CreatedByUser)
            .WithMany()
            .HasForeignKey(t => t.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Ticket -> AssignedToUser Relationship
        builder.Entity<Ticket>()
            .HasOne(t => t.AssignedToUser)
            .WithMany()
            .HasForeignKey(t => t.AssignedToUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Ticket -> PrecedentTicket Relationship (Self-referencing loop)
        builder.Entity<Ticket>()
            .HasOne(t => t.PrecedentTicket)
            .WithMany(t => t.ChildTickets)
            .HasForeignKey(t => t.PrecedentTicketId)
            .OnDelete(DeleteBehavior.Restrict);

        // 4. GLOBAL QUERY FILTERS (Automated Multi-tenancy and Soft Delete)
        // EF Core will dynamically evaluate _currentUserService.CompanyId at query execution time.

        // Filter for Core Transactional Entities (Must belong to the current Tenant AND not be deleted)
        builder.Entity<Ticket>().HasQueryFilter(t => t.GcRecord == 0 && t.CompanyId == _currentUserService.CompanyId);
        builder.Entity<Customer>().HasQueryFilter(c => c.GcRecord == 0 && c.CompanyId == _currentUserService.CompanyId);
        builder.Entity<CustomerAddress>().HasQueryFilter(ca => ca.GcRecord == 0 && ca.CompanyId == _currentUserService.CompanyId);
        builder.Entity<CustomerContact>().HasQueryFilter(cc => cc.GcRecord == 0 && cc.CompanyId == _currentUserService.CompanyId);
        builder.Entity<Project>().HasQueryFilter(p => p.GcRecord == 0 && p.CompanyId == _currentUserService.CompanyId);
        builder.Entity<Area>().HasQueryFilter(a => a.GcRecord == 0 && a.CompanyId == _currentUserService.CompanyId);

        // Filter for Global Catalogs (Only Soft Delete, as they are shared across all Tenants)
        builder.Entity<Country>().HasQueryFilter(c => c.GcRecord == 0);
        builder.Entity<Region>().HasQueryFilter(r => r.GcRecord == 0);
        builder.Entity<Province>().HasQueryFilter(p => p.GcRecord == 0);
        builder.Entity<Municipality>().HasQueryFilter(m => m.GcRecord == 0);
        builder.Entity<TicketStatus>().HasQueryFilter(ts => ts.GcRecord == 0);
        builder.Entity<TicketComplexity>().HasQueryFilter(tc => tc.GcRecord == 0);
        builder.Entity<TimeUnit>().HasQueryFilter(tu => tu.GcRecord == 0);
        builder.Entity<CommunicationChannel>().HasQueryFilter(cc => cc.GcRecord == 0);
    }

    /// <summary>
    /// Configures options for the context, such as injecting interceptors.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Inject the Auditing and Multi-tenant interceptor into the EF Core pipeline
        optionsBuilder.AddInterceptors(_auditableInterceptor);
        
        // Enable sensitive data logging to aid in debugging complex LINQ queries (Disable in Production)
        optionsBuilder.EnableSensitiveDataLogging();
        
        base.OnConfiguring(optionsBuilder);
    }
}