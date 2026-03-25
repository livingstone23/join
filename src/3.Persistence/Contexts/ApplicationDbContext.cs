// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Reflection;
using JOIN.Application.Interface;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using JOIN.Domain.Support;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Persistence.Contexts;

/// <summary>
/// Core database context for the JOIN CRM.
/// Inherits from IdentityDbContext to support ASP.NET Core Identity with Guid keys.
/// Enforces Multi-Tenancy (CompanyId) and Soft Delete (GcRecord) globally.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    private readonly AuditableEntitySaveChangesInterceptor _auditableInterceptor;
    private readonly ICurrentUserService _currentUserService;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        AuditableEntitySaveChangesInterceptor auditableInterceptor,
        ICurrentUserService currentUserService) : base(options)
    {
        _auditableInterceptor = auditableInterceptor;
        _currentUserService = currentUserService;
    }

    // --- 2. ADMINISTRATIVE MODULE ---
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<CompanyModule> CompanyModules => Set<CompanyModule>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<CustomerContact> CustomerContacts => Set<CustomerContact>();
    public DbSet<EntityStatus> EntityStatuses => Set<EntityStatus>();
    public DbSet<IdentificationType> IdentificationTypes => Set<IdentificationType>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<RoleSystemOption> RoleSystemOptions => Set<RoleSystemOption>();
    public DbSet<SystemModule> SystemModules => Set<SystemModule>();
    public DbSet<SystemOption> SystemOptions => Set<SystemOption>();


    // --- 3. COMMON MODULE ---
    public DbSet<CommunicationChannel> CommunicationChannels => Set<CommunicationChannel>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<Municipality> Municipalities => Set<Municipality>();
    public DbSet<Province> Provinces => Set<Province>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<StreetType> StreetTypes => Set<StreetType>();
 

    // --- 4. MESSAGING MODULE ---
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketComplexity> TicketComplexities => Set<TicketComplexity>();
    public DbSet<TicketStatus> TicketStatuses => Set<TicketStatus>();
    public DbSet<TimeUnit> TimeUnits => Set<TimeUnit>();
    public DbSet<UserCommunicationChannel> UserCommunicationChannels => Set<UserCommunicationChannel>();


    // --- 5. SECURITY MODULE ---
    public DbSet<ApplicationRole> ApplicationRoles => Set<ApplicationRole>();
    public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();
    public DbSet<UserCompany> UserCompanies => Set<UserCompany>();
    public DbSet<UserConnectionLog> UserConnectionLogs => Set<UserConnectionLog>();
    public DbSet<UserCustomer> UserCustomers => Set<UserCustomer>();
    public DbSet<UserRoleCompany> UserRoleCompanies => Set<UserRoleCompany>();
    
    
    /// --- 6. SUPPORT
    public DbSet<TicketNotification> TicketNotifications => Set<TicketNotification>();








    
    /// <summary>
    /// Configures the database schema, relationships, and global query filters.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // 1. IMPORTANT: Call base to configure Identity tables (AspNetUsers, etc.)
        base.OnModelCreating(builder);

        // 2. Apply Fluent API configurations from Configuration/ folder automatically
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // 3. CONFIGURE GLOBAL FILTERS (Soft Delete & Multi-Tenancy)
        ConfigureGlobalQueryFilters(builder);

        // 4. SQL SERVER FIX: Prevent multiple cascade paths for Ticket entity
        ConfigureTicketRelationships(builder);

        // 5. SQL SERVER FIX: Prevent multiple cascade paths for Security relationships
        ConfigureSecurityRelationships(builder);
    }

    /// <summary>
    /// Applies global filters to all relevant entities.
    /// </summary>
    private void ConfigureGlobalQueryFilters(ModelBuilder builder)
{
    // --- 1. TENANT-SPECIFIC & SOFT DELETE ENTITIES ---
    // These entities MUST belong to the current Company and NOT be deleted.
    
    // Administrative / Customers
    builder.Entity<Customer>().HasQueryFilter(e => e.GcRecord == 0 && e.CompanyId == _currentUserService.CompanyId);
    builder.Entity<CustomerAddress>().HasQueryFilter(e => e.GcRecord == 0 && e.CompanyId == _currentUserService.CompanyId);
    builder.Entity<CustomerContact>().HasQueryFilter(e => e.GcRecord == 0 && e.CompanyId == _currentUserService.CompanyId);
    builder.Entity<Project>().HasQueryFilter(e => e.GcRecord == 0 && e.CompanyId == _currentUserService.CompanyId);
    builder.Entity<Area>().HasQueryFilter(e => e.GcRecord == 0 && e.CompanyId == _currentUserService.CompanyId);
    
    // Messaging / Tickets
    builder.Entity<Ticket>().HasQueryFilter(e => e.GcRecord == 0 && e.CompanyId == _currentUserService.CompanyId);
    builder.Entity<TicketNotification>().HasQueryFilter(e => e.GcRecord == 0 && e.CompanyId == _currentUserService.CompanyId);


    // --- 2. SHARED CATALOGS (SOFT DELETE ONLY) ---
    // These are global or system-wide catalogs where we only care if they are deleted.

    // Common Module
    builder.Entity<Company>().HasQueryFilter(e => e.GcRecord == 0);
    builder.Entity<Country>().HasQueryFilter(e => e.GcRecord == 0);
    builder.Entity<Region>().HasQueryFilter(e => e.GcRecord == 0);
    builder.Entity<Province>().HasQueryFilter(e => e.GcRecord == 0);
    builder.Entity<Municipality>().HasQueryFilter(e => e.GcRecord == 0);
    builder.Entity<StreetType>().HasQueryFilter(e => e.GcRecord == 0);
    builder.Entity<CommunicationChannel>().HasQueryFilter(e => e.GcRecord == 0);
    
    // Admin / Support Catalogs
    builder.Entity<EntityStatus>().HasQueryFilter(e => e.GcRecord == 0);
    builder.Entity<IdentificationType>().HasQueryFilter(e => e.GcRecord == 0);
    builder.Entity<TicketStatus>().HasQueryFilter(e => e.GcRecord == 0);
    builder.Entity<TicketComplexity>().HasQueryFilter(e => e.GcRecord == 0);
    builder.Entity<TimeUnit>().HasQueryFilter(e => e.GcRecord == 0);
    
    // System Configuration
    builder.Entity<SystemModule>().HasQueryFilter(e => e.GcRecord == 0);
    builder.Entity<SystemOption>().HasQueryFilter(e => e.GcRecord == 0);
    builder.Entity<CompanyModule>().HasQueryFilter(e => e.GcRecord == 0);
    builder.Entity<RoleSystemOption>().HasQueryFilter(e => e.GcRecord == 0);


    // --- 3. SECURITY & INTERSECTION ENTITIES (SOFT DELETE) ---
    // These link users to tenants or customers and should respect the delete flag.
    
    builder.Entity<UserCompany>().HasQueryFilter(e => e.GcRecord == 0);
    builder.Entity<UserCustomer>().HasQueryFilter(e => e.GcRecord == 0);
    builder.Entity<UserRoleCompany>().HasQueryFilter(e => e.GcRecord == 0);
    builder.Entity<UserCommunicationChannel>().HasQueryFilter(e => e.GcRecord == 0);
    
    // Note: UserConnectionLog typically doesn't need Soft Delete as it's an audit trail,
    // but if it inherits from BaseAuditableEntity, add it here:
    // builder.Entity<UserConnectionLog>().HasQueryFilter(e => e.GcRecord == 0);
}

    /// <summary>
    /// Manages complex relationships for the Ticket entity to avoid SQL Server circular reference errors.
    /// </summary>
    private void ConfigureTicketRelationships(ModelBuilder builder)
    {
        // Avoid cycles on CreatedByUserId
        builder.Entity<Ticket>()
            .HasOne(t => t.CreatedByUser)
            .WithMany()
            .HasForeignKey(t => t.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Avoid cycles on AssignedToUserId
        builder.Entity<Ticket>()
            .HasOne(t => t.AssignedToUser)
            .WithMany()
            .HasForeignKey(t => t.AssignedToUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Self-reference handling for child/precedent tickets
        builder.Entity<Ticket>()
            .HasOne(t => t.PrecedentTicket)
            .WithMany(t => t.ChildTickets)
            .HasForeignKey(t => t.PrecedentTicketId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private void ConfigureSecurityRelationships(ModelBuilder builder)
    {
        // Prevent delete cycles between Users and Companies
        builder.Entity<UserCompany>()
            .HasOne(uc => uc.User)
            .WithMany()
            .HasForeignKey(uc => uc.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<UserCompany>()
            .HasOne(uc => uc.Company)
            .WithMany()
            .HasForeignKey(uc => uc.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Prevent delete cycles between Users and Customers
        builder.Entity<UserCustomer>()
            .HasOne(uc => uc.Customer)
            .WithMany()
            .HasForeignKey(uc => uc.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>
    /// Configures the DbContext behavior, including interceptors and logging.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Add the Auditing Interceptor injected via constructor
        optionsBuilder.AddInterceptors(_auditableInterceptor);
        
        // Useful for debugging in development environment
        optionsBuilder.EnableSensitiveDataLogging();
        
        base.OnConfiguring(optionsBuilder);
    }
}