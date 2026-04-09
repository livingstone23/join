using JOIN.Domain.Admin;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using System.Linq;



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

            // 2. Organizaciones (Tenants)
            var joinCompanyId = await SeedDefaultCompanyAsync();
            var privateCompanyId = await SeedPrivateCompanyAsync();

            // 3. Catálogos administrativos
            var idTypeId = await SeedIdentificationTypesAsync();

            // 4. Catálogos geográficos y de comunicación
            await SeedCountriesAsync();
            await SeedProvincesAsync();
            await SeedMunicipalitiesAsync();
            await SeedStreetTypesAsync();
            await SeedCommunicationChannelsAsync();
            await SeedSystemModulesAsync();
            await SeedCompanyModulesAsync();
            await SeedSystemOptionsAsync();

            // 5. Identidad de acceso y autorizacion inicial
            await SeedDefaultUsersAsync();
            await SeedUserAccessAsync(joinCompanyId);
            await SeedRoleSystemOptionsAsync(joinCompanyId);

            // 6. Catálogos administrativos operacionales
            var activeStatusId = await SeedActiveEntityStatusAsync();
            await SeedAreasByCompanyAsync(joinCompanyId, privateCompanyId, activeStatusId);
            await SeedProjectsByCompanyAsync(joinCompanyId, privateCompanyId, activeStatusId);

            // 7. Entidades operacionales (Clientes)
            await SeedJoinCustomersAsync(joinCompanyId, idTypeId);
            await SeedPrivateCustomersAsync(privateCompanyId, idTypeId);
            await SeedJoinCustomerAddressesAndContactsAsync(joinCompanyId);

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
        string[] roles = { "SuperAdmin", "Admin", "Agent", "Customer", "Manager", "Supervisor", "Coordinador", "UsuarioSimple" };

        foreach (var roleName in roles)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                _logger.LogDebug("Creating role: {RoleName}", roleName);
                var createResult = await _roleManager.CreateAsync(new ApplicationRole
                {
                    Name = roleName,
                    NormalizedName = roleName.ToUpperInvariant(),
                    Description = $"Default system role for {roleName}",
                    IsSystemDefault = true,
                    Created = DateTime.UtcNow,
                    CreatedBy = "System_Seeder",
                    GcRecord = 0
                });

                EnsureIdentityResultSucceeded(createResult, $"creating role '{roleName}'");
                continue;
            }

            var hasChanges = role.Description != $"Default system role for {roleName}"
                || !role.IsSystemDefault
                || role.GcRecord != 0;

            if (!hasChanges)
            {
                continue;
            }

            role.Description = $"Default system role for {roleName}";
            role.IsSystemDefault = true;
            role.GcRecord = 0;
            role.LastModified = DateTime.UtcNow;
            role.LastModifiedBy = "System_Seeder";

            var updateResult = await _roleManager.UpdateAsync(role);
            EnsureIdentityResultSucceeded(updateResult, $"updating role '{roleName}'");
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

    private async Task<Guid> SeedPrivateCompanyAsync()
    {
        var existing = await _context.Companies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TaxId == "PRIV-001");

        if (existing == null)
        {
            _logger.LogDebug("Inserting Private Company...");
            var company = new Company
            {
                Name = "Private Company",
                TaxId = "PRIV-001",
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

    private async Task SeedDefaultUsersAsync()
    {
        foreach (var seed in GetDefaultUserSeeds())
        {
            var user = await _userManager.FindByEmailAsync(seed.Email);
            var isNewUser = user is null;

            user ??= new ApplicationUser
            {
                UserName = seed.Email,
                Email = seed.Email,
                Created = DateTime.UtcNow,
                CreatedBy = "System_Seeder",
                GcRecord = 0
            };

            user.FirstName = seed.FirstName;
            user.LastName = seed.LastName;
            user.UserName = seed.Email;
            user.Email = seed.Email;
            user.EmailConfirmed = true;
            user.IsActive = true;
            user.IsSuperAdmin = seed.IsSuperAdmin;
            user.IsSuperAdminCompany = false;
            user.GcRecord = 0;

            if (isNewUser)
            {
                var createResult = await _userManager.CreateAsync(user, seed.Password);
                EnsureIdentityResultSucceeded(createResult, $"creating user '{seed.Email}'");
            }
            else
            {
                user.LastModified = DateTime.UtcNow;
                user.LastModifiedBy = "System_Seeder";

                var updateResult = await _userManager.UpdateAsync(user);
                EnsureIdentityResultSucceeded(updateResult, $"updating user '{seed.Email}'");

                IdentityResult passwordResult;
                if (await _userManager.HasPasswordAsync(user))
                {
                    var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
                    passwordResult = await _userManager.ResetPasswordAsync(user, resetToken, seed.Password);
                }
                else
                {
                    passwordResult = await _userManager.AddPasswordAsync(user, seed.Password);
                }

                EnsureIdentityResultSucceeded(passwordResult, $"setting password for '{seed.Email}'");
            }

            if (!await _userManager.IsInRoleAsync(user, seed.RoleName))
            {
                var addToRoleResult = await _userManager.AddToRoleAsync(user, seed.RoleName);
                EnsureIdentityResultSucceeded(addToRoleResult, $"assigning role '{seed.RoleName}' to '{seed.Email}'");
            }
        }
    }

    private async Task SeedUserAccessAsync(Guid joinCompanyId)
    {
        var seeds = GetDefaultUserSeeds();
        var emails = seeds.Select(x => x.Email).ToList();
        var roleNames = seeds.Select(x => x.RoleName).Distinct().ToList();

        var users = await _context.ApplicationUsers
            .IgnoreQueryFilters()
            .Where(u => u.Email != null && emails.Contains(u.Email))
            .ToListAsync();

        var roles = await _context.ApplicationRoles
            .IgnoreQueryFilters()
            .Where(r => r.Name != null && roleNames.Contains(r.Name))
            .ToListAsync();

        var usersByEmail = users.ToDictionary(u => u.Email!, StringComparer.OrdinalIgnoreCase);
        var roleIdsByName = roles.ToDictionary(r => r.Name!, r => r.Id, StringComparer.OrdinalIgnoreCase);
        var userIds = users.Select(u => u.Id).ToList();

        var existingUserCompanies = await _context.UserCompanies
            .IgnoreQueryFilters()
            .Where(uc => uc.CompanyId == joinCompanyId && userIds.Contains(uc.UserId))
            .ToListAsync();

        var existingUserRoleCompanies = await _context.UserRoleCompanies
            .IgnoreQueryFilters()
            .Where(urc => urc.CompanyId == joinCompanyId && userIds.Contains(urc.UserId))
            .ToListAsync();

        var userCompanyByUserId = existingUserCompanies.ToDictionary(x => x.UserId, x => x);
        var userRoleSet = existingUserRoleCompanies
            .Select(x => (x.UserId, x.RoleId, x.CompanyId))
            .ToHashSet();

        var now = DateTime.UtcNow;
        var companyLinksInserted = 0;
        var roleLinksInserted = 0;

        foreach (var seed in seeds)
        {
            if (!usersByEmail.TryGetValue(seed.Email, out var user))
            {
                _logger.LogWarning("Seeded user {Email} was not found while linking company access.", seed.Email);
                continue;
            }

            if (!userCompanyByUserId.TryGetValue(user.Id, out var userCompany))
            {
                _context.UserCompanies.Add(new UserCompany
                {
                    UserId = user.Id,
                    CompanyId = joinCompanyId,
                    IsDefault = true,
                    Created = now,
                    CreatedBy = "System_Seeder",
                    GcRecord = 0
                });

                companyLinksInserted++;
            }
            else if (!userCompany.IsDefault || userCompany.GcRecord != 0)
            {
                userCompany.IsDefault = true;
                userCompany.GcRecord = 0;
                userCompany.LastModified = now;
                userCompany.LastModifiedBy = "System_Seeder";
            }

            if (!roleIdsByName.TryGetValue(seed.RoleName, out var roleId))
            {
                _logger.LogWarning("Seeded role {RoleName} was not found while linking company access.", seed.RoleName);
                continue;
            }

            if (userRoleSet.Contains((user.Id, roleId, joinCompanyId)))
            {
                continue;
            }

            _context.UserRoleCompanies.Add(new UserRoleCompany
            {
                UserId = user.Id,
                RoleId = roleId,
                CompanyId = joinCompanyId,
                Created = now,
                CreatedBy = "System_Seeder",
                GcRecord = 0
            });

            roleLinksInserted++;
        }

        if (companyLinksInserted > 0 || roleLinksInserted > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation(
            "User access seed finished. UserCompanies inserted: {UserCompaniesInserted}, UserRoleCompanies inserted: {UserRoleCompaniesInserted}",
            companyLinksInserted,
            roleLinksInserted);
    }

    private static void EnsureIdentityResultSucceeded(IdentityResult result, string action)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));
        throw new InvalidOperationException($"Identity seeding failed while {action}. Details: {errors}");
    }

    private async Task<Guid> SeedActiveEntityStatusAsync()
    {
        var now = DateTime.UtcNow;

        var seeds = new[]
        {
            new { Code = 1, Name = "Active", Description = "Default active status for operational entities", IsOperative = true },
            new { Code = 2, Name = "Pausada", Description = "Status used when the entity is temporarily paused.", IsOperative = false },
            new { Code = 3, Name = "Bloqueado", Description = "Status used when the entity is blocked and cannot operate.", IsOperative = false },
            new { Code = 4, Name = "PendienteIniciar", Description = "Status used when the entity is pending start.", IsOperative = false }
        };

        var existingStatuses = await _context.EntityStatuses
            .IgnoreQueryFilters()
            .ToListAsync();

        var hasChanges = false;

        foreach (var seed in seeds)
        {
            var existing = existingStatuses.FirstOrDefault(s => s.Code == seed.Code);
            if (existing != null)
            {
                if (!string.Equals(existing.Name, seed.Name, StringComparison.Ordinal)
                    || !string.Equals(existing.Description, seed.Description, StringComparison.Ordinal)
                    || existing.IsOperative != seed.IsOperative
                    || existing.GcRecord != 0)
                {
                    existing.Name = seed.Name;
                    existing.Description = seed.Description;
                    existing.IsOperative = seed.IsOperative;
                    existing.GcRecord = 0;
                    hasChanges = true;
                }

                continue;
            }

            var status = new EntityStatus
            {
                Name = seed.Name,
                Description = seed.Description,
                Code = seed.Code,
                IsOperative = seed.IsOperative,
                Created = now,
                CreatedBy = "System_Seeder",
                GcRecord = 0
            };

            _context.EntityStatuses.Add(status);
            existingStatuses.Add(status);
            hasChanges = true;
        }

        if (hasChanges)
        {
            await _context.SaveChangesAsync();
        }

        return existingStatuses.First(s => s.Code == 1).Id;
    }

    private async Task SeedAreasByCompanyAsync(Guid joinCompanyId, Guid privateCompanyId, Guid entityStatusId)
    {
        var seeds = new List<(Guid CompanyId, string Name)>
        {
            // JOIN
            (joinCompanyId, "Operaciones"),
            (joinCompanyId, "Administracion"),
            (joinCompanyId, "IT"),
            (joinCompanyId, "Ventas"),
            (joinCompanyId, "Contabilidad"),
            (joinCompanyId, "Atencion al Cliente"),

            // Private Company
            (privateCompanyId, "Contabilidad"),
            (privateCompanyId, "Ventas"),
            (privateCompanyId, "AtencionCliente"),
            (privateCompanyId, "IT")
        };

        var inserted = 0;

        foreach (var seed in seeds)
        {
            var exists = await _context.Areas
                .IgnoreQueryFilters()
                .AnyAsync(a => a.CompanyId == seed.CompanyId && a.Name == seed.Name);

            if (exists)
            {
                continue;
            }

            _context.Areas.Add(new Area
            {
                CompanyId = seed.CompanyId,
                Name = seed.Name,
                EntityStatusId = entityStatusId,
                Created = DateTime.UtcNow,
                CreatedBy = "System_Seeder",
                GcRecord = 0
            });

            inserted++;
        }

        if (inserted > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("Areas seed finished. Inserted: {Inserted}, Existing: {Existing}", inserted, seeds.Count - inserted);
    }

    private async Task SeedProjectsByCompanyAsync(Guid joinCompanyId, Guid privateCompanyId, Guid entityStatusId)
    {
        var seeds = new List<(Guid CompanyId, string Name)>
        {
            // JOIN
            (joinCompanyId, "Ventas2026"),
            (joinCompanyId, "NewWeb"),
            (joinCompanyId, "Soporte"),
            (joinCompanyId, "AtencionCliente"),
            (joinCompanyId, "Compras"),
            (joinCompanyId, "Contabilidad"),
            (joinCompanyId, "MejorasIT"),
            (joinCompanyId, "SoporteIt"),

            // Private Company
            (privateCompanyId, "Soporte"),
            (privateCompanyId, "AtencionCliente"),
            (privateCompanyId, "NewSystem")
        };

        var inserted = 0;

        foreach (var seed in seeds)
        {
            var exists = await _context.Projects
                .IgnoreQueryFilters()
                .AnyAsync(p => p.CompanyId == seed.CompanyId && p.Name == seed.Name);

            if (exists)
            {
                continue;
            }

            _context.Projects.Add(new Project
            {
                CompanyId = seed.CompanyId,
                Name = seed.Name,
                EntityStatusId = entityStatusId,
                Created = DateTime.UtcNow,
                CreatedBy = "System_Seeder",
                GcRecord = 0
            });

            inserted++;
        }

        if (inserted > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("Projects seed finished. Inserted: {Inserted}, Existing: {Existing}", inserted, seeds.Count - inserted);
    }

    private async Task SeedJoinCustomersAsync(Guid companyId, Guid idTypeId)
    {
        var seeds = GetJoinCustomerSeeds();
        var inserted = 0;

        foreach (var seed in seeds)
        {
            var exists = await _context.Customers
                .IgnoreQueryFilters()
                .AnyAsync(c => c.CompanyId == companyId && c.IdentificationNumber == seed.IdentificationNumber);

            if (exists) continue;

            _context.Customers.Add(new Customer
            {
                CompanyId = companyId,
                PersonType = seed.PersonType,
                FirstName = seed.FirstName,
                LastName = seed.LastName ?? string.Empty,
                CommercialName = seed.CommercialName,
                IdentificationTypeId = idTypeId,
                IdentificationNumber = seed.IdentificationNumber,
                Created = DateTime.UtcNow,
                CreatedBy = "System_Seeder",
                GcRecord = 0
            });
            inserted++;
        }

        if (inserted > 0)
            await _context.SaveChangesAsync();

        _logger.LogInformation("JOIN customers seed finished. Inserted: {Inserted}, Existing: {Existing}", inserted, seeds.Count - inserted);
    }

    private async Task SeedPrivateCustomersAsync(Guid companyId, Guid idTypeId)
    {
        var seeds = GetPrivateCustomerSeeds();
        var inserted = 0;

        foreach (var seed in seeds)
        {
            var exists = await _context.Customers
                .IgnoreQueryFilters()
                .AnyAsync(c => c.CompanyId == companyId && c.IdentificationNumber == seed.IdentificationNumber);

            if (exists) continue;

            _context.Customers.Add(new Customer
            {
                CompanyId = companyId,
                PersonType = seed.PersonType,
                FirstName = seed.FirstName,
                LastName = seed.LastName ?? string.Empty,
                CommercialName = seed.CommercialName,
                IdentificationTypeId = idTypeId,
                IdentificationNumber = seed.IdentificationNumber,
                Created = DateTime.UtcNow,
                CreatedBy = "System_Seeder",
                GcRecord = 0
            });
            inserted++;
        }

        if (inserted > 0)
            await _context.SaveChangesAsync();

        _logger.LogInformation("Private Company customers seed finished. Inserted: {Inserted}, Existing: {Existing}", inserted, seeds.Count - inserted);
    }

    private async Task SeedJoinCustomerAddressesAndContactsAsync(Guid companyId)
    {
        var joinSeeds = GetJoinCustomerSeeds().Take(30).ToList();
        var identificationNumbers = joinSeeds
            .Select(x => x.IdentificationNumber)
            .ToList();

        var customersByIdNumber = await _context.Customers
            .IgnoreQueryFilters()
            .Where(c => c.CompanyId == companyId && identificationNumbers.Contains(c.IdentificationNumber))
            .ToDictionaryAsync(c => c.IdentificationNumber, c => c);

        var country = await _context.Countries
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.IsoCode == "HN");

        var province = await _context.Provinces
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Code == "HN-CR");

        var municipality = await _context.Municipalities
            .IgnoreQueryFilters()
            .Where(m => m.ProvinceId == (province == null ? Guid.Empty : province.Id))
            .FirstOrDefaultAsync(m => m.Name == "San Pedro Sula");

        var streetType = await _context.StreetTypes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Name == "Avenida");

        if (country == null || province == null || municipality == null || streetType == null)
        {
            _logger.LogWarning("Address/contact seeds skipped because required catalogs (Country/Province/Municipality/StreetType) were not found.");
            return;
        }

        var customerIds = customersByIdNumber.Values.Select(c => c.Id).ToList();

        var existingAddressCounts = await _context.CustomerAddresses
            .IgnoreQueryFilters()
            .Where(a => customerIds.Contains(a.CustomerId))
            .GroupBy(a => a.CustomerId)
            .Select(g => new { CustomerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Count);

        var existingContactCounts = await _context.CustomerContacts
            .IgnoreQueryFilters()
            .Where(c => customerIds.Contains(c.CustomerId))
            .GroupBy(c => c.CustomerId)
            .Select(g => new { CustomerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Count);

        var insertedAddresses = 0;
        var insertedContacts = 0;

        for (var i = 0; i < joinSeeds.Count; i++)
        {
            var seed = joinSeeds[i];
            if (!customersByIdNumber.TryGetValue(seed.IdentificationNumber, out var customer))
            {
                _logger.LogWarning("Customer with identification {IdentificationNumber} was not found for address/contact seeding.", seed.IdentificationNumber);
                continue;
            }

            var targetCount = i < 10 ? 1 : i < 20 ? 2 : 5;
            var currentAddressCount = existingAddressCounts.TryGetValue(customer.Id, out var addressCount) ? addressCount : 0;
            var currentContactCount = existingContactCounts.TryGetValue(customer.Id, out var contactCount) ? contactCount : 0;

            for (var j = currentAddressCount; j < targetCount; j++)
            {
                _context.CustomerAddresses.Add(new CustomerAddress
                {
                    CompanyId = companyId,
                    CustomerId = customer.Id,
                    AddressLine1 = $"Avenida {i + 1:D2}, Bloque {j + 1:D2}",
                    AddressLine2 = $"Colonia Empresarial {((i % 5) + 1):D2}",
                    ZipCode = $"21{i + 1:D2}{j + 1:D2}",
                    StreetTypeId = streetType.Id,
                    CountryId = country.Id,
                    ProvinceId = province.Id,
                    MunicipalityId = municipality.Id,
                    IsDefault = j == 0,
                    Created = DateTime.UtcNow,
                    CreatedBy = "System_Seeder",
                    GcRecord = 0
                });
                insertedAddresses++;
            }

            for (var j = currentContactCount; j < targetCount; j++)
            {
                var contactType = j switch
                {
                    0 => ContactType.PrimaryEmail,
                    1 => ContactType.MobilePhone,
                    2 => ContactType.WhatsApp,
                    3 => ContactType.AlternativeEmail,
                    _ => ContactType.Landline
                };

                var contactValue = contactType switch
                {
                    ContactType.PrimaryEmail => $"cliente{i + 1:D2}@joinseed.com",
                    ContactType.AlternativeEmail => $"cliente{i + 1:D2}.alt@joinseed.com",
                    ContactType.MobilePhone => $"+504900{i + 1:D2}{j + 1:D2}",
                    ContactType.WhatsApp => $"+504950{i + 1:D2}{j + 1:D2}",
                    _ => $"+504220{i + 1:D2}{j + 1:D2}"
                };

                _context.CustomerContacts.Add(new CustomerContact
                {
                    CompanyId = companyId,
                    CustomerId = customer.Id,
                    ContactType = contactType,
                    ContactValue = contactValue,
                    IsPrimary = j == 0,
                    Comments = "Seed generated contact",
                    Created = DateTime.UtcNow,
                    CreatedBy = "System_Seeder",
                    GcRecord = 0
                });

                insertedContacts++;
            }
        }

        if (insertedAddresses > 0 || insertedContacts > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation(
            "JOIN customer address/contact seed finished. Addresses inserted: {AddressesInserted}, Contacts inserted: {ContactsInserted}",
            insertedAddresses,
            insertedContacts);
    }

    private static List<CustomerSeed> GetJoinCustomerSeeds() =>
    [
        // Physical persons (22)
        new(PersonType.Physical, "Carlos",     "101-1980101-1", LastName: "Mendoza"),
        new(PersonType.Physical, "Ana",        "102-1985621-1", LastName: "Rodriguez"),
        new(PersonType.Physical, "Luis",       "101-1990315-1", LastName: "Garcia"),
        new(PersonType.Physical, "Maria",      "103-1978422-1", LastName: "Hernandez"),
        new(PersonType.Physical, "Roberto",    "101-1995814-1", LastName: "Diaz"),
        new(PersonType.Physical, "Sandra",     "104-1983229-1", LastName: "Lopez"),
        new(PersonType.Physical, "Jorge",      "101-1992717-1", LastName: "Ramirez"),
        new(PersonType.Physical, "Claudia",    "102-1987502-1", LastName: "Torres"),
        new(PersonType.Physical, "Ernesto",    "101-1976618-1", LastName: "Reyes"),
        new(PersonType.Physical, "Patricia",   "103-1991116-1", LastName: "Nunez"),
        new(PersonType.Physical, "Francisco",  "101-1984903-1", LastName: "Alvarez"),
        new(PersonType.Physical, "Carmen",     "104-1989312-1", LastName: "Vega"),
        new(PersonType.Physical, "Andres",     "101-1993428-1", LastName: "Moreno"),
        new(PersonType.Physical, "Silvia",     "102-1982705-1", LastName: "Fuentes"),
        new(PersonType.Physical, "Marcelo",    "101-1977811-1", LastName: "Castillo"),
        new(PersonType.Physical, "Diana",      "103-1994021-1", LastName: "Pena"),
        new(PersonType.Physical, "Alejandro",  "101-1986114-1", LastName: "Ruiz"),
        new(PersonType.Physical, "Valeria",    "104-1998527-1", LastName: "Sanchez"),
        new(PersonType.Physical, "Guillermo",  "101-1981630-1", LastName: "Paredes"),
        new(PersonType.Physical, "Natalia",    "102-1996204-1", LastName: "Medina"),
        new(PersonType.Physical, "Raul",       "101-1975907-1", LastName: "Gutierrez"),
        new(PersonType.Physical, "Beatriz",    "103-1999118-1", LastName: "Rios"),
        // Legal entities (8) — FirstName/LastName belong to the legal representative
        new(PersonType.Legal, "Andres",  "0614-01010-10001", LastName: "Peralta",  CommercialName: "TechSolutions Centroamerica S.A."),
        new(PersonType.Legal, "Carmen",  "0614-01010-10002", LastName: "Reyes",    CommercialName: "Importaciones Global S.R.L."),
        new(PersonType.Legal, "Felipe",  "0614-01010-10003", LastName: "Torres",   CommercialName: "Constructora Nacional S.A."),
        new(PersonType.Legal, "Diana",   "0614-01010-10004", LastName: "Morales",  CommercialName: "Grupo Financiero Meridian S.A."),
        new(PersonType.Legal, "Rafael",  "0614-01010-10005", LastName: "Blanco",   CommercialName: "Distribuidora Continental S.A."),
        new(PersonType.Legal, "Lucia",   "0614-01010-10006", LastName: "Carreras", CommercialName: "Servicios Logisticos del Pacifico S.R.L."),
        new(PersonType.Legal, "Hector",  "0614-01010-10007", LastName: "Vargas",   CommercialName: "Inversiones Atlantico S.A."),
        new(PersonType.Legal, "Monica",  "0614-01010-10008", LastName: "Salazar",  CommercialName: "Consultores Estrategicos Unificados S.A.")
    ];

    private static List<CustomerSeed> GetPrivateCustomerSeeds() =>
    [
        // Physical persons (3)
        new(PersonType.Physical, "Michael", "PC-2000001-1", LastName: "Johnson"),
        new(PersonType.Physical, "Sarah",   "PC-2000002-1", LastName: "Williams"),
        new(PersonType.Physical, "James",   "PC-2000003-1", LastName: "Anderson"),
        // Legal entities (2) — FirstName/LastName belong to the legal representative
        new(PersonType.Legal, "Robert", "US-0614-000001", LastName: "Smith",  CommercialName: "Global Services LLC"),
        new(PersonType.Legal, "Emily",  "US-0614-000002", LastName: "Davis",  CommercialName: "Pacific Ventures Corp")
    ];

    private async Task SeedCountriesAsync()
    {
        var seeds = GetCountrySeeds();
        var inserted = 0;

        foreach (var seed in seeds)
        {
            var exists = await _context.Countries
                .IgnoreQueryFilters()
                .AnyAsync(c => c.IsoCode == seed.IsoCode);

            if (exists)
            {
                continue;
            }

            _context.Countries.Add(new Country
            {
                Name = seed.Name,
                IsoCode = seed.IsoCode,
                Created = DateTime.UtcNow,
                CreatedBy = "System_Seeder",
                GcRecord = 0
            });

            inserted++;
        }

        if (inserted > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("Countries catalog seed finished. Inserted: {Inserted}, Existing: {Existing}", inserted, seeds.Count - inserted);
    }

    private async Task SeedProvincesAsync()
    {
        var seeds = GetProvinceSeeds();
        var countryIsoCodes = seeds
            .Select(x => x.CountryIsoCode)
            .Distinct()
            .ToList();

        var countriesByIso = await _context.Countries
            .IgnoreQueryFilters()
            .Where(c => countryIsoCodes.Contains(c.IsoCode))
            .ToDictionaryAsync(c => c.IsoCode, c => c.Id);

        var inserted = 0;

        foreach (var seed in seeds)
        {
            if (!countriesByIso.TryGetValue(seed.CountryIsoCode, out var countryId))
            {
                _logger.LogWarning("Country with ISO {IsoCode} was not found while seeding provinces.", seed.CountryIsoCode);
                continue;
            }

            var exists = await _context.Provinces
                .IgnoreQueryFilters()
                .AnyAsync(p => p.CountryId == countryId && p.Code == seed.Code);

            if (exists)
            {
                continue;
            }

            _context.Provinces.Add(new Province
            {
                CountryId = countryId,
                Name = seed.Name,
                Code = seed.Code,
                Created = DateTime.UtcNow,
                CreatedBy = "System_Seeder",
                GcRecord = 0
            });

            inserted++;
        }

        if (inserted > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("Provinces catalog seed finished. Inserted: {Inserted}, Existing: {Existing}", inserted, seeds.Count - inserted);
    }

    private async Task SeedMunicipalitiesAsync()
    {
        var seeds = GetMunicipalitySeeds();
        var countryIsoCodes = seeds
            .Select(x => x.CountryIsoCode)
            .Distinct()
            .ToList();

        var countriesByIso = await _context.Countries
            .IgnoreQueryFilters()
            .Where(c => countryIsoCodes.Contains(c.IsoCode))
            .ToDictionaryAsync(c => c.IsoCode, c => c.Id);

        var targetCountryIds = countriesByIso.Values.ToList();
        var provincesByCountryAndCode = await _context.Provinces
            .IgnoreQueryFilters()
            .Where(p => targetCountryIds.Contains(p.CountryId))
            .ToDictionaryAsync(p => (p.CountryId, p.Code), p => p.Id);

        var inserted = 0;

        foreach (var seed in seeds)
        {
            if (!countriesByIso.TryGetValue(seed.CountryIsoCode, out var countryId))
            {
                _logger.LogWarning("Country with ISO {IsoCode} was not found while seeding municipalities.", seed.CountryIsoCode);
                continue;
            }

            if (!provincesByCountryAndCode.TryGetValue((countryId, seed.ProvinceCode), out var provinceId))
            {
                _logger.LogWarning(
                    "Province with code {ProvinceCode} for country {IsoCode} was not found while seeding municipalities.",
                    seed.ProvinceCode,
                    seed.CountryIsoCode);
                continue;
            }

            var exists = await _context.Municipalities
                .IgnoreQueryFilters()
                .AnyAsync(m => m.ProvinceId == provinceId && m.Name == seed.Name);

            if (exists)
            {
                continue;
            }

            _context.Municipalities.Add(new Municipality
            {
                ProvinceId = provinceId,
                Name = seed.Name,
                Code = seed.Code,
                Created = DateTime.UtcNow,
                CreatedBy = "System_Seeder",
                GcRecord = 0
            });

            inserted++;
        }

        if (inserted > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("Municipalities catalog seed finished. Inserted: {Inserted}, Existing: {Existing}", inserted, seeds.Count - inserted);
    }

    private async Task SeedStreetTypesAsync()
    {
        var seeds = GetStreetTypeSeeds();
        var inserted = 0;

        foreach (var seed in seeds)
        {
            var exists = await _context.StreetTypes
                .IgnoreQueryFilters()
                .AnyAsync(s => s.Name == seed.Name || s.Abbreviation == seed.Abbreviation);

            if (exists)
            {
                continue;
            }

            _context.StreetTypes.Add(new StreetType
            {
                Name = seed.Name,
                Abbreviation = seed.Abbreviation,
                IsActive = true,
                Created = DateTime.UtcNow,
                CreatedBy = "System_Seeder",
                GcRecord = 0
            });

            inserted++;
        }

        if (inserted > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("Street types catalog seed finished. Inserted: {Inserted}, Existing: {Existing}", inserted, seeds.Count - inserted);
    }

    private async Task SeedCommunicationChannelsAsync()
    {
        var now = DateTime.UtcNow;
        var seeds = new List<CommunicationChannel>
        {
            new() { Name = "SendGrid", Provider = "SendGrid", Code = "SENDGRID", IsActive = true, Created = now, CreatedBy = "System_Seeder", GcRecord = 0 },
            new() { Name = "Telegram", Provider = "Telegram", Code = "TELEGRAM", IsActive = true, Created = now, CreatedBy = "System_Seeder", GcRecord = 0 },
            new() { Name = "Twilio", Provider = "Twilio", Code = "TWILIO", IsActive = true, Created = now, CreatedBy = "System_Seeder", GcRecord = 0 },
            new() { Name = "WhatsApp", Provider = "Meta", Code = "WHATSAPP", IsActive = true, Created = now, CreatedBy = "System_Seeder", GcRecord = 0 }
        };

        var inserted = 0;

        foreach (var seed in seeds)
        {
            var exists = await _context.CommunicationChannels
                .IgnoreQueryFilters()
                .AnyAsync(c => c.Name == seed.Name);

            if (exists)
            {
                continue;
            }

            _context.CommunicationChannels.Add(seed);
            inserted++;
        }

        if (inserted > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("Communication channels catalog seed finished. Inserted: {Inserted}, Existing: {Existing}", inserted, seeds.Count - inserted);
    }

    private async Task SeedSystemModulesAsync()
    {
        var now = DateTime.UtcNow;
        var seeds = new List<SystemModule>
        {
            new() { Name = "Administracion", Description = "Modulo de gestion administrativa", Icon = "fa-solid fa-building", IsActive = true, Created = now, CreatedBy = "System_Seeder", GcRecord = 0 },
            new() { Name = "Seguridad", Description = "Modulo de seguridad y control de acceso", Icon = "fa-solid fa-shield-halved", IsActive = true, Created = now, CreatedBy = "System_Seeder", GcRecord = 0 },
            new() { Name = "Clientes", Description = "Modulo de gestion de clientes", Icon = "fa-solid fa-users", IsActive = true, Created = now, CreatedBy = "System_Seeder", GcRecord = 0 },
            new() { Name = "Tickets", Description = "Modulo de gestion de tickets", Icon = "fa-solid fa-ticket", IsActive = true, Created = now, CreatedBy = "System_Seeder", GcRecord = 0 }
        };

        var inserted = 0;

        foreach (var seed in seeds)
        {
            var exists = await _context.SystemModules
                .IgnoreQueryFilters()
                .AnyAsync(m => m.Name == seed.Name);

            if (exists)
            {
                continue;
            }

            _context.SystemModules.Add(seed);
            inserted++;
        }

        if (inserted > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("System modules catalog seed finished. Inserted: {Inserted}, Existing: {Existing}", inserted, seeds.Count - inserted);
    }

    private async Task SeedCompanyModulesAsync()
    {
        var companyIds = await _context.Companies
            .IgnoreQueryFilters()
            .Where(c => c.IsActive)
            .Select(c => c.Id)
            .ToListAsync();

        if (companyIds.Count == 0)
        {
            _logger.LogWarning("Company modules seed skipped. No active companies were found.");
            return;
        }

        var moduleIds = await _context.SystemModules
            .IgnoreQueryFilters()
            .Where(m => m.IsActive)
            .Select(m => m.Id)
            .ToListAsync();

        if (moduleIds.Count == 0)
        {
            _logger.LogWarning("Company modules seed skipped. No active system modules were found.");
            return;
        }

        var existingPairs = await _context.CompanyModules
            .IgnoreQueryFilters()
            .Where(cm => companyIds.Contains(cm.CompanyId) && moduleIds.Contains(cm.ModuleId))
            .Select(cm => new { cm.CompanyId, cm.ModuleId })
            .ToListAsync();

        var existingSet = existingPairs
            .Select(x => (x.CompanyId, x.ModuleId))
            .ToHashSet();

        var now = DateTime.UtcNow;
        var inserted = 0;

        foreach (var companyId in companyIds)
        {
            foreach (var moduleId in moduleIds)
            {
                if (existingSet.Contains((companyId, moduleId)))
                {
                    continue;
                }

                _context.CompanyModules.Add(new CompanyModule
                {
                    CompanyId = companyId,
                    ModuleId = moduleId,
                    IsActive = true,
                    Created = now,
                    CreatedBy = "System_Seeder",
                    GcRecord = 0
                });

                inserted++;
            }
        }

        if (inserted > 0)
        {
            await _context.SaveChangesAsync();
        }

        var totalExpected = companyIds.Count * moduleIds.Count;
        _logger.LogInformation(
            "Company modules seed finished. Inserted: {Inserted}, Existing: {Existing}, TotalExpected: {TotalExpected}",
            inserted,
            totalExpected - inserted,
            totalExpected);
    }

    private async Task SeedSystemOptionsAsync()
    {
        var adminModule = await _context.SystemModules
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Name == "Administracion");

        if (adminModule is null)
        {
            _logger.LogWarning("System options seed skipped. The 'Administracion' module was not found.");
            return;
        }

        var seeds = GetAdministrativeSystemOptionSeeds();
        var optionNames = seeds.Select(x => x.Name).ToList();
        var existingOptions = await _context.SystemOptions
            .IgnoreQueryFilters()
            .Where(o => o.ModuleId == adminModule.Id && optionNames.Contains(o.Name))
            .ToListAsync();

        var optionsByName = existingOptions.ToDictionary(o => o.Name, StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var inserted = 0;
        var updated = 0;

        foreach (var seed in seeds.OrderBy(x => x.ParentName is null ? 0 : 1))
        {
            Guid? parentId = null;
            if (!string.IsNullOrWhiteSpace(seed.ParentName) && optionsByName.TryGetValue(seed.ParentName, out var parentOption))
            {
                parentId = parentOption.Id;
            }

            if (!optionsByName.TryGetValue(seed.Name, out var option))
            {
                option = new SystemOption
                {
                    ModuleId = adminModule.Id,
                    Name = seed.Name,
                    Route = seed.Route,
                    Icon = seed.Icon,
                    ParentId = parentId,
                    ControllerName = seed.ControllerName,
                    CanRead = seed.CanRead,
                    CanCreate = seed.CanCreate,
                    CanUpdate = seed.CanUpdate,
                    CanDelete = seed.CanDelete,
                    Created = now,
                    CreatedBy = "System_Seeder",
                    GcRecord = 0
                };

                _context.SystemOptions.Add(option);
                optionsByName[seed.Name] = option;
                inserted++;
                continue;
            }

            var hasChanges = option.Route != seed.Route
                || option.Icon != seed.Icon
                || option.ParentId != parentId
                || option.ControllerName != seed.ControllerName
                || option.CanRead != seed.CanRead
                || option.CanCreate != seed.CanCreate
                || option.CanUpdate != seed.CanUpdate
                || option.CanDelete != seed.CanDelete
                || option.GcRecord != 0;

            if (!hasChanges)
            {
                continue;
            }

            option.Route = seed.Route;
            option.Icon = seed.Icon;
            option.ParentId = parentId;
            option.ControllerName = seed.ControllerName;
            option.CanRead = seed.CanRead;
            option.CanCreate = seed.CanCreate;
            option.CanUpdate = seed.CanUpdate;
            option.CanDelete = seed.CanDelete;
            option.GcRecord = 0;
            option.LastModified = now;
            option.LastModifiedBy = "System_Seeder";
            updated++;
        }

        if (inserted > 0 || updated > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation(
            "System options seed finished. Inserted: {Inserted}, Updated: {Updated}, Existing: {Existing}",
            inserted,
            updated,
            seeds.Count - inserted - updated);
    }

    private async Task SeedRoleSystemOptionsAsync(Guid joinCompanyId)
    {
        var seeds = GetRoleSystemOptionSeeds();
        var roleNames = seeds.Select(x => x.RoleName).Distinct().ToList();
        var optionNames = seeds.Select(x => x.SystemOptionName).Distinct().ToList();

        var roles = await _context.ApplicationRoles
            .IgnoreQueryFilters()
            .Where(r => r.Name != null && roleNames.Contains(r.Name))
            .ToListAsync();

        var systemOptions = await _context.SystemOptions
            .IgnoreQueryFilters()
            .Where(o => optionNames.Contains(o.Name))
            .ToListAsync();

        var rolesByName = roles.ToDictionary(r => r.Name!, StringComparer.OrdinalIgnoreCase);
        var optionsByName = systemOptions.ToDictionary(o => o.Name, StringComparer.OrdinalIgnoreCase);

        var existingPermissions = await _context.RoleSystemOptions
            .IgnoreQueryFilters()
            .Where(rso => rso.CompanyId == joinCompanyId)
            .ToListAsync();

        var permissionByKey = existingPermissions.ToDictionary(x => (x.RoleId, x.SystemOptionId));
        var now = DateTime.UtcNow;
        var inserted = 0;
        var updated = 0;

        foreach (var seed in seeds)
        {
            if (!rolesByName.TryGetValue(seed.RoleName, out var role))
            {
                _logger.LogWarning("Role permission seed skipped because role {RoleName} was not found.", seed.RoleName);
                continue;
            }

            if (!optionsByName.TryGetValue(seed.SystemOptionName, out var option))
            {
                _logger.LogWarning("Role permission seed skipped because system option {SystemOptionName} was not found.", seed.SystemOptionName);
                continue;
            }

            if (!permissionByKey.TryGetValue((role.Id, option.Id), out var permission))
            {
                _context.RoleSystemOptions.Add(new RoleSystemOption
                {
                    RoleId = role.Id,
                    SystemOptionId = option.Id,
                    CompanyId = joinCompanyId,
                    CanRead = seed.CanRead,
                    CanCreate = seed.CanCreate,
                    CanUpdate = seed.CanUpdate,
                    CanDelete = seed.CanDelete,
                    Created = now,
                    CreatedBy = "System_Seeder",
                    GcRecord = 0
                });

                inserted++;
                continue;
            }

            var hasChanges = permission.CompanyId != joinCompanyId
                || permission.CanRead != seed.CanRead
                || permission.CanCreate != seed.CanCreate
                || permission.CanUpdate != seed.CanUpdate
                || permission.CanDelete != seed.CanDelete
                || permission.GcRecord != 0;

            if (!hasChanges)
            {
                continue;
            }

            permission.CompanyId = joinCompanyId;
            permission.CanRead = seed.CanRead;
            permission.CanCreate = seed.CanCreate;
            permission.CanUpdate = seed.CanUpdate;
            permission.CanDelete = seed.CanDelete;
            permission.GcRecord = 0;
            permission.LastModified = now;
            permission.LastModifiedBy = "System_Seeder";
            updated++;
        }

        if (inserted > 0 || updated > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation(
            "Role system options seed finished. Inserted: {Inserted}, Updated: {Updated}, Existing: {Existing}",
            inserted,
            updated,
            seeds.Count - inserted - updated);
    }

    private static List<CountrySeed> GetCountrySeeds() =>
    [
        new("Honduras", "HN"),
        new("Nicaragua", "NI"),
        new("Estados Unidos", "US"),
        new("Panama", "PA"),
        new("Espana", "ES")
    ];

    private static List<ProvinceSeed> GetProvinceSeeds() =>
    [
        // Honduras
        new("HN", "Cortes", "HN-CR"),
        new("HN", "Francisco Morazan", "HN-FM"),
        new("HN", "Yoro", "HN-YO"),
        new("HN", "Olancho", "HN-OL"),
        new("HN", "Atlantida", "HN-AT"),
        new("HN", "Comayagua", "HN-CM"),
        new("HN", "Choluteca", "HN-CH"),
        new("HN", "Colon", "HN-CL"),
        new("HN", "El Paraiso", "HN-EP"),
        new("HN", "Copan", "HN-CP"),

        // Nicaragua
        new("NI", "Managua", "NI-MN"),
        new("NI", "Leon", "NI-LE"),
        new("NI", "Matagalpa", "NI-MT"),
        new("NI", "Chinandega", "NI-CH"),
        new("NI", "Masaya", "NI-MS"),
        new("NI", "Esteli", "NI-ES"),
        new("NI", "Jinotega", "NI-JI"),
        new("NI", "Granada", "NI-GR"),
        new("NI", "Carazo", "NI-CA"),
        new("NI", "Rivas", "NI-RI"),

        // Estados Unidos
        new("US", "California", "US-CA"),
        new("US", "Texas", "US-TX"),
        new("US", "Florida", "US-FL"),
        new("US", "New York", "US-NY"),
        new("US", "Pennsylvania", "US-PA"),
        new("US", "Illinois", "US-IL"),
        new("US", "Ohio", "US-OH"),
        new("US", "Georgia", "US-GA"),
        new("US", "North Carolina", "US-NC"),
        new("US", "Michigan", "US-MI"),

        // Panama
        new("PA", "Panama", "PA-PA"),
        new("PA", "Panama Oeste", "PA-WP"),
        new("PA", "Chiriqui", "PA-CH"),
        new("PA", "Cocle", "PA-CO"),
        new("PA", "Colon", "PA-CL"),
        new("PA", "Veraguas", "PA-VR"),
        new("PA", "Bocas del Toro", "PA-BT"),
        new("PA", "Darien", "PA-DR"),
        new("PA", "Herrera", "PA-HE"),
        new("PA", "Los Santos", "PA-LS"),

        // Espana
        new("ES", "Madrid", "ES-M"),
        new("ES", "Barcelona", "ES-B"),
        new("ES", "Valencia", "ES-V"),
        new("ES", "Sevilla", "ES-SE"),
        new("ES", "Alicante", "ES-A"),
        new("ES", "Malaga", "ES-MA"),
        new("ES", "Murcia", "ES-MU"),
        new("ES", "Cadiz", "ES-CA"),
        new("ES", "Bizkaia", "ES-BI"),
        new("ES", "Las Palmas", "ES-GC")
    ];

    private static List<MunicipalitySeed> GetMunicipalitySeeds() =>
    [
        // Honduras (20)
        new("HN", "HN-CR", "San Pedro Sula"),
        new("HN", "HN-CR", "Choloma"),
        new("HN", "HN-CR", "Puerto Cortes"),
        new("HN", "HN-CR", "Villanueva"),
        new("HN", "HN-FM", "Distrito Central"),
        new("HN", "HN-FM", "Talanga"),
        new("HN", "HN-FM", "Valle de Angeles"),
        new("HN", "HN-FM", "Santa Lucia"),
        new("HN", "HN-YO", "El Progreso"),
        new("HN", "HN-YO", "Yoro"),
        new("HN", "HN-YO", "Olanchito"),
        new("HN", "HN-YO", "Morazan"),
        new("HN", "HN-AT", "La Ceiba"),
        new("HN", "HN-AT", "Tela"),
        new("HN", "HN-AT", "Jutiapa"),
        new("HN", "HN-AT", "Arizona"),
        new("HN", "HN-CM", "Comayagua"),
        new("HN", "HN-CM", "Siguatepeque"),
        new("HN", "HN-CM", "Taulabe"),
        new("HN", "HN-CM", "Villa de San Antonio"),

        // Nicaragua (20)
        new("NI", "NI-MN", "Managua"),
        new("NI", "NI-MN", "Ciudad Sandino"),
        new("NI", "NI-MN", "Tipitapa"),
        new("NI", "NI-MN", "Ticuantepe"),
        new("NI", "NI-LE", "Leon"),
        new("NI", "NI-LE", "Nagarote"),
        new("NI", "NI-LE", "La Paz Centro"),
        new("NI", "NI-LE", "Quezalguaque"),
        new("NI", "NI-MT", "Matagalpa"),
        new("NI", "NI-MT", "Matiguas"),
        new("NI", "NI-MT", "Sebaco"),
        new("NI", "NI-MT", "Rio Blanco"),
        new("NI", "NI-CH", "Chinandega"),
        new("NI", "NI-CH", "Corinto"),
        new("NI", "NI-CH", "El Viejo"),
        new("NI", "NI-CH", "Chichigalpa"),
        new("NI", "NI-MS", "Masaya"),
        new("NI", "NI-MS", "Nindiri"),
        new("NI", "NI-MS", "Catarina"),
        new("NI", "NI-MS", "Niquinohomo"),

        // Estados Unidos (20)
        new("US", "US-CA", "Los Angeles"),
        new("US", "US-CA", "San Diego"),
        new("US", "US-CA", "San Jose"),
        new("US", "US-CA", "San Francisco"),
        new("US", "US-TX", "Houston"),
        new("US", "US-TX", "Dallas"),
        new("US", "US-TX", "Austin"),
        new("US", "US-TX", "San Antonio"),
        new("US", "US-FL", "Jacksonville"),
        new("US", "US-FL", "Miami"),
        new("US", "US-FL", "Tampa"),
        new("US", "US-FL", "Orlando"),
        new("US", "US-NY", "New York City"),
        new("US", "US-NY", "Buffalo"),
        new("US", "US-NY", "Rochester"),
        new("US", "US-NY", "Yonkers"),
        new("US", "US-IL", "Chicago"),
        new("US", "US-IL", "Aurora"),
        new("US", "US-IL", "Naperville"),
        new("US", "US-IL", "Joliet"),

        // Panama (20)
        new("PA", "PA-PA", "Panama City"),
        new("PA", "PA-PA", "San Miguelito"),
        new("PA", "PA-PA", "Chepo"),
        new("PA", "PA-PA", "Taboga"),
        new("PA", "PA-WP", "La Chorrera"),
        new("PA", "PA-WP", "Arraijan"),
        new("PA", "PA-WP", "Chame"),
        new("PA", "PA-WP", "San Carlos"),
        new("PA", "PA-CH", "David"),
        new("PA", "PA-CH", "Boquete"),
        new("PA", "PA-CH", "Bugaba"),
        new("PA", "PA-CH", "Dolega"),
        new("PA", "PA-CL", "Colon"),
        new("PA", "PA-CL", "Chagres"),
        new("PA", "PA-CL", "Donoso"),
        new("PA", "PA-CL", "Portobelo"),
        new("PA", "PA-CO", "Penonome"),
        new("PA", "PA-CO", "Aguadulce"),
        new("PA", "PA-CO", "Anton"),
        new("PA", "PA-CO", "La Pintada"),

        // Espana (20)
        new("ES", "ES-M", "Madrid"),
        new("ES", "ES-M", "Mostoles"),
        new("ES", "ES-M", "Alcala de Henares"),
        new("ES", "ES-M", "Fuenlabrada"),
        new("ES", "ES-B", "Barcelona"),
        new("ES", "ES-B", "LHospitalet de Llobregat"),
        new("ES", "ES-B", "Badalona"),
        new("ES", "ES-B", "Terrassa"),
        new("ES", "ES-V", "Valencia"),
        new("ES", "ES-V", "Torrent"),
        new("ES", "ES-V", "Gandia"),
        new("ES", "ES-V", "Paterna"),
        new("ES", "ES-SE", "Sevilla"),
        new("ES", "ES-SE", "Dos Hermanas"),
        new("ES", "ES-SE", "Alcala de Guadaira"),
        new("ES", "ES-SE", "Utrera"),
        new("ES", "ES-MA", "Malaga"),
        new("ES", "ES-MA", "Marbella"),
        new("ES", "ES-MA", "Velez-Malaga"),
        new("ES", "ES-MA", "Fuengirola")
    ];

    private static List<StreetTypeSeed> GetStreetTypeSeeds() =>
    [
        new("Avenida", "Av."),
        new("Calle", "C."),
        new("Boulevard", "Blvd."),
        new("Pasaje", "Psj."),
        new("Carretera", "Carr."),
        new("Camino", "Cno."),
        new("Autopista", "Aut."),
        new("Diagonal", "Diag."),
        new("Transversal", "Trans."),
        new("Circunvalacion", "Circ."),
        new("Plaza", "Plz."),
        new("Paseo", "Pso."),
        new("Sendero", "Snd."),
        new("Via", "V."),
        new("Callejon", "Cjon.")
    ];

    private static List<DefaultUserSeed> GetDefaultUserSeeds() =>
    [
        new("livingstone", "bravo", "lcano@join.com", "ABCabc123*", "SuperAdmin", true),
        new("Manager", "Test", "manager@join.com", "ABCabc123*", "Manager", false),
        new("Supervisor", "Test", "supervisor@join.com", "ABCabc123*", "Supervisor", false),
        new("SimpleUser", "Test", "simpleuser@join.com", "ABCabc123*", "UsuarioSimple", false)
    ];

    private static List<SystemOptionSeed> GetAdministrativeSystemOptionSeeds() =>
    [
        new("Administracion", "/administracion", "icon_admin", null, null, false, false, false, false),
        new("Paises", "/administracion/countries", "icon_country", "Administracion", "Countries", true, true, true, true),
        new("Regions", "/administracion/regions", "icon_region", "Administracion", "Regions", true, true, true, true),
        new("Provinces", "/administracion/provinces", "icon_province", "Administracion", "Provinces", true, true, true, true),
        new("Municipalities", "/administracion/municipalities", "icon_municipality", "Administracion", "Municipalities", true, true, true, true),
        new("IdentificationTypes", "/administracion/identification-types", "icon_identification_type", "Administracion", "IdentificationTypes", true, true, true, true),
        new("Areas", "/administracion/areas", "icon_area", "Administracion", "Areas", true, true, true, true),
        new("Projects", "/administracion/projects", "icon_project", "Administracion", "Projects", true, true, true, true),
        new("SystemModules"  , "/administracion/system-modules"  , "icon_system_module", "Administracion", "SystemModules", true, true, true, true),
        new("EntityStatuses" , "/administracion/entity-statuses" , "icon_entity_status", "Administracion", "EntityStatuses", true, true, true, true),
        new("Compañias"      , "/administracion/companies"       , "icon_company"      ,  "Administracion", "Companies"    , true, true, true, true),
        new("CompanyModules", "/administracion/company-modules", "icon_company_module", "Administracion", "CompanyModules", true, true, true, true),
        new("CommunicationChannels", "/administracion/communication-channels", "icon_channel", "Administracion", "CommunicationChannels", true, true, true, true),
        
        new("Tickets", "/tickets", "icon_ticket", null, null, false, false, false, false),
        new("TimeUnits", "/administracion/time-units", "icon_time_unit", "Administracion", "TimeUnits", true, true, true, true),
        new("TicketStatuses", "/administracion/ticket-statuses", "icon_ticket_status", "Administracion", "TicketStatuses", true, true, true, true)

    ];

    private static List<RoleSystemOptionSeed> GetRoleSystemOptionSeeds() =>
    [
        new("SuperAdmin", "SystemModules", true, true, true, true),
        new("SuperAdmin", "TimeUnits", true, true, true, true),
        new("SuperAdmin", "TicketStatuses", true, true, true, true),
        new("Admin", "Administracion", false, false, false, false),
        new("Admin", "TimeUnits", true, true, true, true),
        new("Admin", "TicketStatuses", true, true, true, true),

        new("Manager", "Administracion", false, false, false, false),
        new("Manager", "Paises", true, true, true, true),
        new("Manager", "Regions", true, true, true, true),
        new("Manager", "Provinces", true, true, true, true),
        new("Manager", "Municipalities", true, true, true, true),
        new("Manager", "IdentificationTypes", true, true, true, true),
        new("Manager", "Areas", true, true, true, true),
        new("Manager", "Projects", true, true, true, true),
        new("Manager", "EntityStatuses", true, true, true, true),
        new("Manager", "CompanyModules", true, true, true, true),
        new("Manager", "CommunicationChannels", true, true, true, true),
        new("Manager", "Compañias", true, true, true, true),
        new("Manager", "TimeUnits", true, true, true, true),
        new("Manager", "TicketStatuses", true, true, true, true),

        new("Supervisor", "Administracion", false, false, false, false),
        new("Supervisor", "Paises", true, true, true, false),
        new("Supervisor", "Regions", true, true, true, false),
        new("Supervisor", "Provinces", true, true, true, false),
        new("Supervisor", "Municipalities", true, true, true, false),
        new("Supervisor", "IdentificationTypes", true, true, true, false),
        new("Supervisor", "Areas", true, true, true, false),
        new("Supervisor", "Projects", true, true, true, false),
        new("Supervisor", "EntityStatuses", true, true, true, false),
        new("Supervisor", "CompanyModules", true, true, true, false),
        new("Supervisor", "CommunicationChannels", true, true, true, false),
        new("Supervisor", "Compañias", true, true, true, false),
        new("Supervisor", "TimeUnits", true, true, true, false),
        new("Supervisor", "TicketStatuses", true, true, true, false),

        // `UsuarioSimple` receives read-only access to selected administrative options while the remaining entries stay restricted by default.
        new("UsuarioSimple", "Administracion", false, false, false, false),
        new("UsuarioSimple", "Paises", false, false, false, false),
        new("UsuarioSimple", "Regions", false, false, false, false),
        new("UsuarioSimple", "Provinces", false, false, false, false),
        new("UsuarioSimple", "Municipalities", false, false, false, false),
        new("UsuarioSimple", "IdentificationTypes", true, false, false, false),
        new("UsuarioSimple", "Areas", true, false, false, false),
        new("UsuarioSimple", "Projects", true, false, false, false),
        new("UsuarioSimple", "EntityStatuses", true, false, false, false),
        new("UsuarioSimple", "CompanyModules", true, false, false, false),
        new("UsuarioSimple", "CommunicationChannels", false, false, false, false),
        new("UsuarioSimple", "Compañias", false, false, false, false),
        new("UsuarioSimple", "TimeUnits", false, false, false, false),
        new("UsuarioSimple", "TicketStatuses", true, false, false, false)
    ];

    private sealed record CountrySeed(string Name, string IsoCode);
    private sealed record ProvinceSeed(string CountryIsoCode, string Name, string Code);
    private sealed record MunicipalitySeed(string CountryIsoCode, string ProvinceCode, string Name, string? Code = null);
    private sealed record StreetTypeSeed(string Name, string Abbreviation);
    private sealed record CustomerSeed(
        PersonType PersonType,
        string FirstName,
        string IdentificationNumber,
        string? LastName = null,
        string? CommercialName = null);
    private sealed record DefaultUserSeed(
        string FirstName,
        string LastName,
        string Email,
        string Password,
        string RoleName,
        bool IsSuperAdmin);
    private sealed record SystemOptionSeed(
        string Name,
        string Route,
        string? Icon,
        string? ParentName,
        string? ControllerName,
        bool CanRead,
        bool CanCreate,
        bool CanUpdate,
        bool CanDelete);
    private sealed record RoleSystemOptionSeed(
        string RoleName,
        string SystemOptionName,
        bool CanRead,
        bool CanCreate,
        bool CanUpdate,
        bool CanDelete);
}



