


using System.Reflection;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using JOIN.Domain.Support;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;



namespace JOIN.Infrastructure.Contexts;



/// <summary>
/// Core database context for the JOIN application.
/// Integrates ASP.NET Core Identity with custom multi-tenant entities.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

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

    // --- Seguridad & Multi-Tenant ---
    public DbSet<UserCompany> UserCompanies => Set<UserCompany>();
    public DbSet<UserRoleCompany> UserRoleCompanies => Set<UserRoleCompany>();
    public DbSet<UserCustomer> UserCustomers => Set<UserCustomer>();

    // --- Omnicanalidad & Tickets (Soporte) ---
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketStatus> TicketStatuses => Set<TicketStatus>();
    public DbSet<TicketComplexity> TicketComplexities => Set<TicketComplexity>();
    public DbSet<TimeUnit> TimeUnits => Set<TimeUnit>();
    public DbSet<TicketNotification> TicketNotifications => Set<TicketNotification>();
    public DbSet<CommunicationChannel> CommunicationChannels => Set<CommunicationChannel>();
    public DbSet<UserCommunicationChannel> UserCommunicationChannels => Set<UserCommunicationChannel>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Obligatorio llamar al base para que Identity cree sus tablas (AspNetUsers, AspNetRoles, etc.)
        base.OnModelCreating(builder); 
        
        // Escanea y aplica todas las clases de configuración (Fluent API) en este ensamblado
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}