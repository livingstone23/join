using JOIN.Domain.Admin;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;



namespace JOIN.Persistence;



/// <summary>
/// Infrastructure class responsible for initial persistence and master data orchestration.
/// </summary>
/// <remarks>
/// <para>
/// This class implements the "Data Seeder" pattern to ensure the database is functional 
/// immediately after creation. It follows a strict referential integrity order:
/// </para>
/// <list type="number">
/// <item><description>Security Roles: The foundation of Role-Based Access Control (RBAC).</description></item>
/// <item><description>Support Entities: Default Company and catalogs (IdentificationTypes) required by the model.</description></item>
/// <item><description>Initial Users: SuperAdmin linked to the created roles.</description></item>
/// <item><description>Operational Data: Sample customers to validate the Multi-tenant workflow.</description></item>
/// </list>
/// <para>
/// Architecture Note: IDs are not assigned manually because <see cref="JOIN.Domain.Common.BaseEntity"/> 
/// handles automatic generation within its protected constructor.
/// </para>
/// </remarks>
public class DatabaseSeeder
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        ApplicationDbContext context, 
        UserManager<ApplicationUser> userManager, 
        RoleManager<ApplicationRole> roleManager,
        ILogger<DatabaseSeeder> logger)
    {
        _context = context;
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        _logger.LogInformation("🚀 Starting master data seeding process...");

        try
        {
            // 1. Roles del sistema
            await SeedRolesAsync();

            // 2. Organización principal (Tenant Raíz)
            var companyId = await SeedDefaultCompanyAsync();

            // 3. Catálogos administrativos
            var idTypeId = await SeedIdentificationTypesAsync();

            // 4. Identidad de acceso (SuperAdmin)
            await SeedSuperAdminAsync();

            // 5. Entidades operacionales (Cliente de prueba)
            await SeedSampleCustomerAsync(companyId, idTypeId);

            // Guardado final para asegurar consistencia
            await _context.SaveChangesAsync();
            _logger.LogInformation("✅ Database seeding completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "❌ Fatal error during the seeding process.");
            throw;
        }
    }

    private async Task SeedRolesAsync()
    {
        string[] roles = { "SuperAdmin", "Admin", "Agent", "Customer" };
        foreach (var roleName in roles)
        {
            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                _logger.LogDebug("Creating role: {RoleName}", roleName);
                await _roleManager.CreateAsync(new ApplicationRole 
                { 
                    Name = roleName, 
                    NormalizedName = roleName.ToUpper(),
                    Description = $"Default system role for {roleName}",
                    IsSystemDefault = true 
                });
            }
        }
    }

    private async Task<Guid> SeedDefaultCompanyAsync()
    {
        // Se usa IgnoreQueryFilters para detectar la empresa aunque no haya contexto de usuario
        var existing = await _context.Companies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TaxId == "JOIN-001");

        if (existing == null)
        {
            _logger.LogDebug("Inserting Master Company...");
            var company = new Company 
            { 
                Name = "JOIN Software Group", 
                TaxId = "JOIN-001",
                IsActive = true,
                Created = DateTime.UtcNow,
                GcRecord = 0 
            };
            _context.Companies.Add(company);
            await _context.SaveChangesAsync(); 
            return company.Id;
        }
        return existing.Id;
    }

    private async Task<Guid> SeedIdentificationTypesAsync()
    {
        // Evita duplicados en catálogos globales
        var existing = await _context.IdentificationTypes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Name == "Cedula");

        if (existing == null)
        {
            _logger.LogDebug("Inserting Identification Types catalog...");
            var idType = new IdentificationType 
            { 
                Name = "Cedula", 
                Description = "National Identification Document", 
                Created = DateTime.UtcNow, 
                GcRecord = 0 
            };
            _context.IdentificationTypes.Add(idType);
            await _context.SaveChangesAsync();
            return idType.Id;
        }
        return existing.Id;
    }

    private async Task SeedSuperAdminAsync()
    {
        var adminEmail = "admin@join.com";
        if (await _userManager.FindByEmailAsync(adminEmail) == null)
        {
            _logger.LogDebug("Creating default SuperAdmin user...");
            var user = new ApplicationUser 
            { 
                UserName = adminEmail, 
                Email = adminEmail, 
                EmailConfirmed = true, 
                IsActive = true,
                Created = DateTime.UtcNow,
                CreatedBy = "System_Seeder"
            };

            var result = await _userManager.CreateAsync(user, "Join_Admin2026*");
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(user, "SuperAdmin");
            }
        }
    }

    private async Task SeedSampleCustomerAsync(Guid companyId, Guid idTypeId)
    {
        // CRÍTICO: Verificamos por identificación única ignorando los filtros globales.
        // Esto evita que AnyAsync devuelva 'false' si el registro ya existe en DB.
        var exists = await _context.Customers
            .IgnoreQueryFilters()
            .AnyAsync(c => c.IdentificationNumber == "402-0000000-1" && c.CompanyId == companyId);

        if (!exists)
        {
            _logger.LogDebug("Inserting sample customer...");
            _context.Customers.Add(new Customer
            {
                CompanyId = companyId,
                PersonType = PersonType.Physical,
                FirstName = "Manuel",
                LastName = "Lcano",
                IdentificationTypeId = idTypeId,
                IdentificationNumber = "402-0000000-1",
                Created = DateTime.UtcNow,
                CreatedBy = "System_Seeder",
                GcRecord = 0
            });
        }
    }
}



