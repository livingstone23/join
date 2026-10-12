using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JOIN.Application.Interface;
using JOIN.Domain.Admin;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;



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
public class DatabaseSeeder : ICompanyCatalogSeeder
{
    /// <summary>
    /// System roles created by <see cref="SeedRolesAsync"/>. Also read by
    /// <see cref="ComputeMenuPermissionsSeedChecksum"/> so the checksum stays in sync with the
    /// actual seed source — do not duplicate this list elsewhere.
    /// </summary>
    private static readonly string[] SystemRoleNames =
        { "SuperAdmin", "SuperAdminCompany", "Admin", "Agent", "Person", "Manager", "Supervisor", "Coordinador", "UsuarioSimple" };

    /// <summary>
    /// Roles granted full access to every system option by <see cref="SeedRoleSystemOptionsAsync"/>.
    /// Also read by <see cref="ComputeMenuPermissionsSeedChecksum"/> — do not duplicate this list elsewhere.
    /// </summary>
    private static readonly string[] PrivilegedRoleNames = { "Admin", "SuperAdminCompany" };

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

    /// <summary>
    /// Seeds the default tenant-aware messaging catalogs for a specific company.
    /// </summary>
    public async Task SeedDefaultCatalogsForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        await SeedMessagingCatalogsForCompanyAsync(companyId, cancellationToken);
        await SeedCalendarCatalogsForCompanyAsync(companyId, cancellationToken);
    }

    /// <summary>
    /// Re-runs system option and role permission seeds idempotently for the master tenant.
    /// Skipped when <paramref name="forceReseed"/> is <c>false</c> and the checksum of the
    /// in-code seed definitions still matches the one stored in <c>Security.SeedState</c>.
    /// </summary>
    /// <param name="forceReseed">
    /// When <c>true</c>, ignores any cached checksum and always runs the full seed pipeline.
    /// Wired from <c>Seeding:ForceMenuPermissionsReseed</c> in configuration.
    /// </param>
    /// <param name="cancellationToken">Cancellation token propagated to EF Core calls.</param>
    public async Task SeedMenuAndPermissionsAsync(bool forceReseed = false, CancellationToken cancellationToken = default)
    {
        var joinCompanyId = await _context.Companies
            .IgnoreQueryFilters()
            .Where(c => c.TaxId == "JOIN-001")
            .Select(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (joinCompanyId == Guid.Empty)
        {
            _logger.LogWarning("Menu and permissions seed skipped. Master company JOIN-001 was not found.");
            return;
        }

        var currentChecksum = ComputeMenuPermissionsSeedChecksum();

        if (!forceReseed)
        {
            var existingState = await _context.SeedStates
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.CompanyId == joinCompanyId, cancellationToken);

            if (existingState is not null && existingState.Checksum == currentChecksum)
            {
                _logger.LogInformation("Menu and permissions seed skipped for company {CompanyId}: no changes detected.", joinCompanyId);
                return;
            }
        }

        await SeedRolesAsync();
        await SeedDefaultUsersAsync();
        await SeedUserAccessAsync(joinCompanyId);
        // SPEC 43: modules first, so an existing database gets the renames and IsBase before the options
        // are re-pointed at their real module.
        await SeedSystemModulesAsync();
        await SeedSystemOptionsAsync();
        await SeedRoleSystemOptionsAsync(joinCompanyId);

        await UpsertSeedStateAsync(joinCompanyId, currentChecksum, cancellationToken);

        _logger.LogInformation("Menu and permissions seed completed for company {CompanyId}.", joinCompanyId);
    }

    /// <summary>
    /// Computes a deterministic SHA-256 checksum over the static in-code definitions that feed
    /// <see cref="SeedMenuAndPermissionsAsync"/>. Used to skip the reseed when nothing changed.
    /// </summary>
    /// <remarks>
    /// Determinism rules: only ordered collections (arrays / <see cref="List{T}"/>) are used;
    /// records are serialized with default <see cref="JsonSerializerOptions"/> which preserves
    /// declared property order. Never replace any input collection here with a <c>Dictionary</c>
    /// or <c>HashSet</c> or the checksum will drift between runs.
    /// </remarks>
    private static string ComputeMenuPermissionsSeedChecksum()
    {
        var snapshot = new
        {
            Roles = SystemRoleNames,
            DefaultUsers = GetDefaultUserSeeds(),
            // SPEC 43: modules (name, IsBase, order) and each option's ModuleName feed the checksum too.
            SystemModules = GetSystemModuleSeeds(),
            AdministrativeSystemOptions = GetAdministrativeSystemOptionSeeds(),
            RoleSystemOptions = GetRoleSystemOptionSeeds(),
            AdminFullSystemOptionPermissions = GetAdminFullSystemOptionPermissionSeeds(),
            PrivilegedRoleNames
        };

        // Compact, no whitespace, no BOM — same payload byte-for-byte every run.
        var jsonOptions = new JsonSerializerOptions { WriteIndented = false };
        var payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, jsonOptions);

        var hash = SHA256.HashData(payload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Inserts or updates the <see cref="SeedState"/> row for the given company, recording the
    /// checksum that was just applied and the time at which it was applied.
    /// </summary>
    private async Task UpsertSeedStateAsync(Guid companyId, string checksum, CancellationToken cancellationToken)
    {
        var existing = await _context.SeedStates
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        var now = DateTime.UtcNow;

        if (existing is null)
        {
            _context.SeedStates.Add(new SeedState
            {
                CompanyId = companyId,
                Checksum = checksum,
                LastAppliedAt = now
            });
        }
        else
        {
            existing.Checksum = checksum;
            existing.LastAppliedAt = now;
        }

        await _context.SaveChangesAsync(cancellationToken);
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
            var defaultTimeUnitId = await SeedMessagingCatalogsForCompanyAsync(joinCompanyId);
            await SeedMessagingCatalogsForCompanyAsync(privateCompanyId);
            await SeedCalendarGlobalCatalogsAsync();
            await SeedCalendarCatalogsForCompanyAsync(joinCompanyId);
            await SeedCalendarCatalogsForCompanyAsync(privateCompanyId);
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
            await SeedGendersAsync(joinCompanyId);
            await SeedTaxRegimesAsync(joinCompanyId);
            await SeedIndustriesAsync(joinCompanyId);
            await SeedIncomeRangesAsync(joinCompanyId);
            await SeedTicketCompanyDefaultsAsync(joinCompanyId, defaultTimeUnitId);
            await SeedTicketUserCompaniesAsync(joinCompanyId);
            await SeedTicketAttachmentSettingsAsync(joinCompanyId);
            await SeedTicketAttachmentSettingsAsync(privateCompanyId);
            await SeedJoinCalendarAsync(joinCompanyId);

            // 7. Entidades operacionales (Clientes)
            await SeedJoinPersonsAsync(joinCompanyId, idTypeId);
            await SeedPrivatePersonsAsync(privateCompanyId, idTypeId);
            await SeedJoinPersonAddressesAndContactsAsync(joinCompanyId);
            await SeedJoinTicketsAsync(joinCompanyId);

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
        foreach (var roleName in SystemRoleNames)
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
            user.IsSuperAdminCompany = seed.IsSuperAdminCompany;
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
        var roleNames = seeds
            .Select(x => x.RoleName)
            .Append("SuperAdminCompany")
            .Distinct()
            .ToList();

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

        if (usersByEmail.TryGetValue("livingstone23@gmail.com", out var superAdminCompanyUser)
            && roleIdsByName.TryGetValue("SuperAdminCompany", out var superAdminCompanyRoleId)
            && !userRoleSet.Contains((superAdminCompanyUser.Id, superAdminCompanyRoleId, joinCompanyId)))
        {
            _context.UserRoleCompanies.Add(new UserRoleCompany
            {
                UserId = superAdminCompanyUser.Id,
                RoleId = superAdminCompanyRoleId,
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

    private async Task SeedGendersAsync(Guid joinCompanyId)
    {
        var seeds = new List<string>
        {
            "Masculino",
            "Femenino"
        };

        var inserted = 0;

        foreach (var seedName in seeds)
        {
            var exists = await _context.Genders
                .IgnoreQueryFilters()
                .AnyAsync(g => g.CompanyId == joinCompanyId && g.Name == seedName);

            if (exists)
            {
                continue;
            }

            var entity = Gender.Create(joinCompanyId, seedName);
            entity.Created = DateTime.UtcNow;
            entity.CreatedBy = "System_Seeder";

            _context.Genders.Add(entity);
            inserted++;
        }

        if (inserted > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("Genders seed finished. Inserted: {Inserted}, Existing: {Existing}", inserted, seeds.Count - inserted);
    }

    private async Task SeedTaxRegimesAsync(Guid joinCompanyId)
    {
        var seeds = new List<(string Code, string Name, string Description)>
        {
            ("SAR-RG", "Régimen General", "Contribuyentes estándar (ISV 15%/18%)."),
            ("SAR-RS", "Régimen Simplificado", "Pequeños contribuyentes."),
            ("SAR-ZOLI", "Régimen ZOLI", "Zonas Libres exentas de impuestos."),
            ("SAR-EX", "Exonerado", "Instituciones con resolución de exención.")
        };

        var inserted = 0;

        foreach (var seed in seeds)
        {
            var exists = await _context.TaxRegimes
                .IgnoreQueryFilters()
                .AnyAsync(tr => tr.CompanyId == joinCompanyId && tr.Code == seed.Code);

            if (exists)
            {
                continue;
            }

            var entity = TaxRegime.Create(joinCompanyId, seed.Code, seed.Name, seed.Description);
            entity.Created = DateTime.UtcNow;
            entity.CreatedBy = "System_Seeder";

            _context.TaxRegimes.Add(entity);
            inserted++;
        }

        if (inserted > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("Tax regimes seed finished. Inserted: {Inserted}, Existing: {Existing}", inserted, seeds.Count - inserted);
    }

    private async Task SeedIndustriesAsync(Guid joinCompanyId)
    {
        var seeds = new List<(string Code, string Name, string Description)>
        {
            ("AGRI", "Agroindustria y Agricultura", "Producción, procesamiento y exportación de productos agrícolas (café, banano, palma africana, azúcar)."),
            ("MAQ", "Manufactura y Maquila", "Industria textil, ensamblaje y manufactura ligera, operando frecuentemente bajo regímenes de zona libre."),
            ("LOG", "Logística y Transporte", "Navieras, aduanas, transporte de carga terrestre y operaciones portuarias (clave en el corredor logístico regional)."),
            ("TOUR", "Turismo y Hospitalidad", "Hotelería, agencias de viaje, restaurantes y operadores turísticos."),
            ("RET", "Comercio Mayorista y Minorista", "Distribución de bienes de consumo masivo (FMCG), supermercados, ferreterías y tiendas por departamento."),
            ("FIN", "Servicios Financieros", "Bancos comerciales, cooperativas de ahorro y crédito, aseguradoras y emergentes Fintech."),
            ("BPO", "Tecnología y BPO / Call Centers", "Servicios de tercerización (BPO/KPO), soporte técnico, atención al cliente y desarrollo de software."),
            ("CONS", "Construcción y Bienes Raíces", "Desarrolladoras inmobiliarias, constructoras de obra civil y despachos de arquitectura."),
            ("HLTH", "Salud y Farmacéutica", "Hospitales privados, clínicas, laboratorios y distribución de insumos médicos."),
            ("PROF", "Servicios Profesionales", "Consultoría legal, contable, auditoría, agencias de marketing y recursos humanos.")
        };

        var inserted = 0;

        foreach (var seed in seeds)
        {
            var exists = await _context.Industries
                .IgnoreQueryFilters()
                .AnyAsync(i => i.CompanyId == joinCompanyId && i.Code == seed.Code);

            if (exists)
            {
                continue;
            }

            var entity = Industry.Create(joinCompanyId, seed.Code, seed.Name, seed.Description);
            entity.Created = DateTime.UtcNow;
            entity.CreatedBy = "System_Seeder";

            _context.Industries.Add(entity);
            inserted++;
        }

        if (inserted > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("Industries seed finished. Inserted: {Inserted}, Existing: {Existing}", inserted, seeds.Count - inserted);
    }

    private async Task SeedIncomeRangesAsync(Guid joinCompanyId)
    {
        var seeds = new List<(int DisplayOrder, string DisplayName, decimal MinimumValue, decimal? MaximumValue, string Currency)>
        {
            (1, "$0 - $500 USD", 0.00m, 500.00m, "USD"),
            (2, "$501 - $1,500 USD", 501.00m, 1500.00m, "USD"),
            (3, "$1,501 - $3,000 USD", 1501.00m, 3000.00m, "USD"),
            (4, "$3,001 - $5,000 USD", 3001.00m, 5000.00m, "USD"),
            (5, "Más de $5,000 USD", 5001.00m, null, "USD")
        };

        var inserted = 0;
        var updated = 0;
        var now = DateTime.UtcNow;

        foreach (var seed in seeds)
        {
            var existing = await _context.IncomeRanges
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(ir => ir.CompanyId == joinCompanyId && ir.DisplayName == seed.DisplayName);

            if (existing is not null)
            {
                if (existing.GcRecord == 0 && existing.DisplayOrder != seed.DisplayOrder)
                {
                    existing.Update(
                        existing.DisplayName,
                        existing.MinimumValue,
                        existing.MaximumValue,
                        existing.CurrencyCode,
                        seed.DisplayOrder);
                    existing.LastModified = now;
                    existing.LastModifiedBy = "System_Seeder";
                    updated++;
                }

                continue;
            }

            var entity = IncomeRange.Create(
                joinCompanyId,
                seed.DisplayName,
                seed.MinimumValue,
                seed.MaximumValue,
                seed.Currency,
                seed.DisplayOrder);
            entity.Created = now;
            entity.CreatedBy = "System_Seeder";

            _context.IncomeRanges.Add(entity);
            inserted++;
        }

        if (inserted > 0 || updated > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation(
            "Income ranges seed finished. Inserted: {Inserted}, Updated: {Updated}, Existing: {Existing}",
            inserted,
            updated,
            seeds.Count - inserted - updated);
    }

    private async Task SeedJoinPersonsAsync(Guid companyId, Guid idTypeId)
    {
        var seeds = GetJoinPersonSeeds();
        var gendersByName = await GetGenderIdsByNameAsync(companyId);
        var inserted = 0;

        foreach (var seed in seeds)
        {
            var exists = await _context.Persons
                .IgnoreQueryFilters()
                .AnyAsync(c => c.CompanyId == companyId && c.IdentificationNumber == seed.IdentificationNumber);

            if (exists) continue;

            var person = new Person
            {
                CompanyId = companyId,
                PersonType = seed.PersonType,
                FirstName = seed.FirstName,
                LastName = seed.LastName ?? string.Empty,
                CommercialName = seed.CommercialName,
                IdentificationTypeId = idTypeId,
                IdentificationNumber = seed.IdentificationNumber,
                GenderId = ResolveGenderId(seed.FirstName, gendersByName),
                Created = DateTime.UtcNow,
                CreatedBy = "System_Seeder",
                GcRecord = 0
            };
            person.Reactivate();

            _context.Persons.Add(person);
            inserted++;
        }

        if (inserted > 0)
            await _context.SaveChangesAsync();

        _logger.LogInformation("JOIN customers seed finished. Inserted: {Inserted}, Existing: {Existing}", inserted, seeds.Count - inserted);
    }

    private async Task SeedPrivatePersonsAsync(Guid companyId, Guid idTypeId)
    {
        var seeds = GetPrivatePersonSeeds();
        var inserted = 0;

        foreach (var seed in seeds)
        {
            var exists = await _context.Persons
                .IgnoreQueryFilters()
                .AnyAsync(c => c.CompanyId == companyId && c.IdentificationNumber == seed.IdentificationNumber);

            if (exists) continue;

            var person = new Person
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
            };
            person.Reactivate();

            _context.Persons.Add(person);
            inserted++;
        }

        if (inserted > 0)
            await _context.SaveChangesAsync();

        _logger.LogInformation("Private Company customers seed finished. Inserted: {Inserted}, Existing: {Existing}", inserted, seeds.Count - inserted);
    }

    private async Task SeedJoinPersonAddressesAndContactsAsync(Guid companyId)
    {
        var joinSeeds = GetJoinPersonSeeds().Take(30).ToList();
        var identificationNumbers = joinSeeds
            .Select(x => x.IdentificationNumber)
            .ToList();

        var customersByIdNumber = await _context.Persons
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

        var existingAddressCounts = await _context.PersonAddresses
            .IgnoreQueryFilters()
            .Where(a => customerIds.Contains(a.PersonId))
            .GroupBy(a => a.PersonId)
            .Select(g => new { PersonId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PersonId, x => x.Count);

        var existingContactCounts = await _context.PersonContacts
            .IgnoreQueryFilters()
            .Where(c => customerIds.Contains(c.PersonId))
            .GroupBy(c => c.PersonId)
            .Select(g => new { PersonId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PersonId, x => x.Count);

        var insertedAddresses = 0;
        var insertedContacts = 0;

        for (var i = 0; i < joinSeeds.Count; i++)
        {
            var seed = joinSeeds[i];
            if (!customersByIdNumber.TryGetValue(seed.IdentificationNumber, out var customer))
            {
                _logger.LogWarning("Person with identification {IdentificationNumber} was not found for address/contact seeding.", seed.IdentificationNumber);
                continue;
            }

            var targetCount = i < 10 ? 1 : i < 20 ? 2 : 5;
            var currentAddressCount = existingAddressCounts.TryGetValue(customer.Id, out var addressCount) ? addressCount : 0;
            var currentContactCount = existingContactCounts.TryGetValue(customer.Id, out var contactCount) ? contactCount : 0;

            for (var j = currentAddressCount; j < targetCount; j++)
            {
                var address = new PersonAddress
                {
                    CompanyId = companyId,
                    PersonId = customer.Id,
                    AddressLine1 = $"Avenida {i + 1:D2}, Bloque {j + 1:D2}",
                    AddressLine2 = $"Colonia Empresarial {((i % 5) + 1):D2}",
                    ZipCode = $"21{i + 1:D2}{j + 1:D2}",
                    StreetTypeId = streetType.Id,
                    CountryId = country.Id,
                    ProvinceId = province.Id,
                    MunicipalityId = municipality.Id,
                    Created = DateTime.UtcNow,
                    CreatedBy = "System_Seeder",
                    GcRecord = 0
                };
                address.Reactivate();
                if (j == 0)
                {
                    address.SetAsDefault();
                }
                _context.PersonAddresses.Add(address);
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

                var contact = PersonContact.Create(
                    companyId,
                    customer.Id,
                    contactType,
                    contactValue,
                    "Seed generated contact");
                contact.Created = DateTime.UtcNow;
                contact.CreatedBy = "System_Seeder";
                if (j == 0)
                {
                    contact.SetAsPrimary();
                }
                _context.PersonContacts.Add(contact);

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

    private static List<PersonSeed> GetJoinPersonSeeds() =>
    [
        // Physical persons (57)
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
        // 35 additional physical persons (SPEC 33 follow-up request)
        new(PersonType.Physical, "Gabriela",   "101-2001001-1", LastName: "Fernandez"),
        new(PersonType.Physical, "Ricardo",    "102-2001002-1", LastName: "Ortiz"),
        new(PersonType.Physical, "Isabel",     "103-2001003-1", LastName: "Campos"),
        new(PersonType.Physical, "Eduardo",    "104-2001004-1", LastName: "Molina"),
        new(PersonType.Physical, "Teresa",     "101-2001005-1", LastName: "Aguilar"),
        new(PersonType.Physical, "Fernando",   "102-2001006-1", LastName: "Cabrera"),
        new(PersonType.Physical, "Rosa",       "103-2001007-1", LastName: "Delgado"),
        new(PersonType.Physical, "Manuel",     "104-2001008-1", LastName: "Espinoza"),
        new(PersonType.Physical, "Cristina",   "101-2001009-1", LastName: "Herrera"),
        new(PersonType.Physical, "Sergio",     "102-2001010-1", LastName: "Ibarra"),
        new(PersonType.Physical, "Adriana",    "103-2001011-1", LastName: "Jimenez"),
        new(PersonType.Physical, "Victor",     "104-2001012-1", LastName: "Leiva"),
        new(PersonType.Physical, "Yolanda",    "101-2001013-1", LastName: "Mora"),
        new(PersonType.Physical, "Ramon",      "102-2001014-1", LastName: "Navarro"),
        new(PersonType.Physical, "Elena",      "103-2001015-1", LastName: "Ochoa"),
        new(PersonType.Physical, "Pablo",      "104-2001016-1", LastName: "Pacheco"),
        new(PersonType.Physical, "Gloria",     "101-2001017-1", LastName: "Quintero"),
        new(PersonType.Physical, "Julio",      "102-2001018-1", LastName: "Rosales"),
        new(PersonType.Physical, "Marta",      "103-2001019-1", LastName: "Salgado"),
        new(PersonType.Physical, "Ivan",       "104-2001020-1", LastName: "Tinoco"),
        new(PersonType.Physical, "Rocio",      "101-2001021-1", LastName: "Urena"),
        new(PersonType.Physical, "Nestor",     "102-2001022-1", LastName: "Villalta"),
        new(PersonType.Physical, "Susana",     "103-2001023-1", LastName: "Zamora"),
        new(PersonType.Physical, "Leonardo",   "104-2001024-1", LastName: "Aguirre"),
        new(PersonType.Physical, "Karla",      "101-2001025-1", LastName: "Bermudez"),
        new(PersonType.Physical, "Danilo",     "102-2001026-1", LastName: "Cordero"),
        new(PersonType.Physical, "Vanessa",    "103-2001027-1", LastName: "Duarte"),
        new(PersonType.Physical, "Oscar",      "104-2001028-1", LastName: "Escobar"),
        new(PersonType.Physical, "Ximena",     "101-2001029-1", LastName: "Figueroa"),
        new(PersonType.Physical, "Wilfredo",   "102-2001030-1", LastName: "Guevara"),
        new(PersonType.Physical, "Daniela",    "103-2001031-1", LastName: "Hidalgo"),
        new(PersonType.Physical, "Mauricio",   "104-2001032-1", LastName: "Iglesias"),
        new(PersonType.Physical, "Fabiola",    "101-2001033-1", LastName: "Lara"),
        new(PersonType.Physical, "Alberto",    "102-2001034-1", LastName: "Marin"),
        new(PersonType.Physical, "Norma",      "103-2001035-1", LastName: "Osorio"),
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

    private static List<PersonSeed> GetPrivatePersonSeeds() =>
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
            new() { Name = "SendGrid", Provider = "SendGrid", Code = CommunicationChannelCodes.SendGrid, IsActive = true, Created = now, CreatedBy = "System_Seeder", GcRecord = 0 },
            new() { Name = "Telegram", Provider = "Telegram", Code = CommunicationChannelCodes.Telegram, IsActive = true, Created = now, CreatedBy = "System_Seeder", GcRecord = 0 },
            new() { Name = "Twilio", Provider = "Twilio", Code = CommunicationChannelCodes.Twilio, IsActive = true, Created = now, CreatedBy = "System_Seeder", GcRecord = 0 },
            new() { Name = "WhatsApp", Provider = "Meta", Code = CommunicationChannelCodes.WhatsApp, IsActive = true, Created = now, CreatedBy = "System_Seeder", GcRecord = 0 },
            // SPEC 99: internal channels for requests born inside JOIN (web console) or a first-party app.
            new() { Name = "Web", Provider = "Internal", Code = CommunicationChannelCodes.Web, IsActive = true, Created = now, CreatedBy = "System_Seeder", GcRecord = 0 },
            new() { Name = "App", Provider = "Internal", Code = CommunicationChannelCodes.App, IsActive = true, Created = now, CreatedBy = "System_Seeder", GcRecord = 0 }
        };

        var inserted = 0;

        foreach (var seed in seeds)
        {
            var existing = await _context.CommunicationChannels
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Name == seed.Name);

            if (existing is not null)
            {
                // SPEC 99: a channel created by hand with the same name but another code is not fixed here
                // (the code drives the Application logic); the CRUD of channels corrects it.
                if (!string.Equals(existing.Code, seed.Code, StringComparison.Ordinal))
                {
                    _logger.LogWarning(
                        "Communication channel '{Name}' exists with code '{Code}' instead of '{ExpectedCode}'. Fix it from the channels catalog.",
                        existing.Name, existing.Code, seed.Code);
                }

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

    /// <summary>
    /// Default channel for seeded ticket data (SPEC 99): <c>WEB</c>, then <c>WHATSAPP</c>, then the oldest active
    /// channel. A deleted <c>WEB</c> is never revived here — only a SuperAdmin restores it (SPEC 41) — so the
    /// seed logs a warning and falls back.
    /// </summary>
    private async Task<CommunicationChannel?> ResolveDefaultTicketChannelAsync()
    {
        var channels = _context.CommunicationChannels.IgnoreQueryFilters();

        var web = await channels.FirstOrDefaultAsync(x => x.Code == CommunicationChannelCodes.Web);
        if (web is { GcRecord: 0 })
        {
            return web;
        }

        if (web is not null)
        {
            _logger.LogWarning(
                "Communication channel {Code} is deleted; ticket seed data falls back to another channel. Restore it with POST /CommunicationChannels/{Id}/restore.",
                CommunicationChannelCodes.Web, web.Id);
        }

        return await channels.FirstOrDefaultAsync(x => x.GcRecord == 0 && x.Code == CommunicationChannelCodes.WhatsApp)
            ?? await channels
                .Where(x => x.GcRecord == 0)
                .OrderBy(x => x.Created)
                .FirstOrDefaultAsync();
    }

    private async Task<Guid> SeedMessagingCatalogsForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var defaultTimeUnitId = await SeedTimeUnitsForCompanyAsync(companyId);
        await SeedTicketStatusesForCompanyAsync(companyId);
        await SeedTicketComplexitiesForCompanyAsync(companyId, defaultTimeUnitId);
        await _context.SaveChangesAsync(cancellationToken);
        return defaultTimeUnitId;
    }

    private async Task<Guid> SeedTimeUnitsForCompanyAsync(Guid companyId)
    {
        var now = DateTime.UtcNow;
        var seeds = new[]
        {
            new { Name = "Hours", Code = 1, IsActive = true },
            new { Name = "Days", Code = 2, IsActive = true }
        };

        var existing = await _context.TimeUnits
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.GcRecord == 0)
            .ToListAsync();

        var inserted = 0;

        foreach (var seed in seeds)
        {
            var alreadyExists = existing.Any(x =>
                x.Code == seed.Code
                || string.Equals(x.Name, seed.Name, StringComparison.OrdinalIgnoreCase));

            if (alreadyExists)
            {
                continue;
            }

            _context.TimeUnits.Add(new TimeUnit
            {
                CompanyId = companyId,
                Name = seed.Name,
                Code = seed.Code,
                IsActive = seed.IsActive,
                Created = now,
                CreatedBy = "System_Seeder",
                GcRecord = 0
            });

            inserted++;
        }

        if (inserted > 0)
        {
            await _context.SaveChangesAsync();
        }

        var defaultTimeUnit = await _context.TimeUnits
            .IgnoreQueryFilters()
            .Where(t => t.CompanyId == companyId && t.GcRecord == 0)
            .OrderBy(t => t.Code)
            .FirstAsync();

        return defaultTimeUnit.Id;
    }

    /// <summary>
    /// Ensures the base ticket statuses exist for the specified company without modifying any previously created records.
    /// </summary>
    /// <param name="companyId">Identifier of the company that will own the default statuses.</param>
    public async Task SeedTicketStatusesForCompanyAsync(Guid companyId)
    {
        var now = DateTime.UtcNow;
        var seeds = new[]
        {
            new { Code = 1, Name = "Inicial", Description = "Initial status for newly created tickets.", IsInitial = true, IsPaused = false, IsFinal = false },
            new { Code = 2, Name = "Paused", Description = "Paused status for tickets temporarily on hold.", IsInitial = false, IsPaused = true, IsFinal = false },
            new { Code = 3, Name = "Final", Description = "Final status for tickets that completed their lifecycle.", IsInitial = false, IsPaused = false, IsFinal = true }
        };

        var existing = await _context.TicketStatuses
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.GcRecord == 0)
            .ToListAsync();

        var hasChanges = false;

        foreach (var seed in seeds)
        {
            var alreadyExists = existing.Any(x =>
                string.Equals(x.Name, seed.Name, StringComparison.OrdinalIgnoreCase)
                || (seed.IsInitial && x.IsInitial)
                || (seed.IsPaused && x.IsPaused)
                || (seed.IsFinal && x.IsFinal));

            if (alreadyExists)
            {
                continue;
            }

            _context.TicketStatuses.Add(new TicketStatus
            {
                CompanyId = companyId,
                Name = seed.Name,
                Description = seed.Description,
                Code = seed.Code,
                IsActive = true,
                IsInitial = seed.IsInitial,
                IsPaused = seed.IsPaused,
                IsFinal = seed.IsFinal,
                Created = now,
                CreatedBy = "System_Seeder",
                GcRecord = 0
            });

            hasChanges = true;
        }

        if (hasChanges)
        {
            await _context.SaveChangesAsync();
        }
    }

    private async Task SeedTicketComplexitiesForCompanyAsync(Guid companyId, Guid defaultTimeUnitId)
    {
        var now = DateTime.UtcNow;
        var seeds = new[]
        {
            new { Code = 1, Name = "Low", Description = "Low complexity ticket.", ResolutionTimeUnits = 8, IsActive = true },
            new { Code = 2, Name = "Medium", Description = "Medium complexity ticket.", ResolutionTimeUnits = 16, IsActive = true },
            new { Code = 3, Name = "High", Description = "High complexity ticket.", ResolutionTimeUnits = 24, IsActive = true }
        };

        var existing = await _context.TicketComplexities
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.GcRecord == 0)
            .ToListAsync();

        var inserted = 0;

        foreach (var seed in seeds)
        {
            var alreadyExists = existing.Any(x =>
                x.Code == seed.Code
                || string.Equals(x.Name, seed.Name, StringComparison.OrdinalIgnoreCase));

            if (alreadyExists)
            {
                continue;
            }

            _context.TicketComplexities.Add(new TicketComplexity
            {
                CompanyId = companyId,
                Name = seed.Name,
                Description = seed.Description,
                Code = seed.Code,
                ResolutionTimeUnits = seed.ResolutionTimeUnits,
                TimeUnitId = defaultTimeUnitId,
                IsActive = seed.IsActive,
                Created = now,
                CreatedBy = "System_Seeder",
                GcRecord = 0
            });

            inserted++;
        }

        if (inserted > 0)
        {
            await _context.SaveChangesAsync();
        }
    }

    private async Task SeedTicketCompanyDefaultsAsync(Guid joinCompanyId, Guid defaultTimeUnitId)
    {
        var openStatusId = await _context.TicketStatuses
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == joinCompanyId && x.GcRecord == 0 && x.IsInitial && x.IsActive && !x.IsFinal)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync();

        var mediumComplexityId = await _context.TicketComplexities
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == joinCompanyId && x.GcRecord == 0 && x.Code == 2)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync();

        var defaultAreaId = await _context.Areas
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == joinCompanyId && x.GcRecord == 0)
            .OrderBy(x => x.Created)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync();

        var defaultProjectId = await _context.Projects
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == joinCompanyId && x.GcRecord == 0)
            .OrderBy(x => x.Created)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync();

        var defaultChannelId = (await ResolveDefaultTicketChannelAsync())?.Id;

        if (!openStatusId.HasValue
            || !mediumComplexityId.HasValue
            || !defaultAreaId.HasValue
            || !defaultProjectId.HasValue
            || !defaultChannelId.HasValue)
        {
            _logger.LogWarning("Ticket company default seed skipped because one or more required references were not available for JOIN.");
            return;
        }

        // SPEC 99-post: only the ACTIVE configuration is updated. A deleted one is never revived (SPEC 41-post rule,
        // same as the WEB channel): reviving it next to another active row broke ux_ticketcompanydefaults_company_active.
        var existing = await _context.TicketCompanyDefaults
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.CompanyId == joinCompanyId && x.GcRecord == 0);

        var now = DateTime.UtcNow;

        if (existing is null)
        {
            if (await _context.TicketCompanyDefaults.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == joinCompanyId))
            {
                _logger.LogWarning(
                    "JOIN ticket company defaults exist only as deleted rows; the seed creates a new active configuration and leaves the deleted ones as they are.");
            }

            _context.TicketCompanyDefaults.Add(new TicketCompanyDefault
            {
                CompanyId = joinCompanyId,
                StartCode = "TICK",
                CodeSequenceLength = 6,
                UsePersonalizedCode = true,
                TicketStatusDefaultId = openStatusId.Value,
                TicketComplexityDefaultId = mediumComplexityId.Value,
                TimeUnitDefaultId = defaultTimeUnitId,
                AreaDefaultId = defaultAreaId.Value,
                ProjectDefaultId = defaultProjectId.Value,
                ChannelDefaultId = defaultChannelId.Value,
                Created = now,
                CreatedBy = "System_Seeder",
                GcRecord = 0
            });

            await _context.SaveChangesAsync();
            _logger.LogInformation("JOIN ticket company defaults seed finished. Inserted: 1, Updated: 0");
            return;
        }

        existing.StartCode = "TICK";
        existing.CodeSequenceLength = 6;
        existing.UsePersonalizedCode = true;
        existing.TicketStatusDefaultId = openStatusId.Value;
        existing.TicketComplexityDefaultId = mediumComplexityId.Value;
        existing.TimeUnitDefaultId = defaultTimeUnitId;
        existing.AreaDefaultId = defaultAreaId.Value;
        existing.ProjectDefaultId = defaultProjectId.Value;
        existing.ChannelDefaultId = defaultChannelId.Value;
        existing.LastModified = now;
        existing.LastModifiedBy = "System_Seeder";

        await _context.SaveChangesAsync();
        _logger.LogInformation("JOIN ticket company defaults seed finished. Inserted: 0, Updated: 1");
    }

    private async Task SeedTicketUserCompaniesAsync(Guid joinCompanyId)
    {
        var managerUser = await _context.ApplicationUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == "manager@join.com");

        var simpleUser = await _context.ApplicationUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == "simpleuser@join.com");

        if (managerUser is null || simpleUser is null)
        {
            _logger.LogWarning("Ticket roster seed skipped because default users manager@join.com and/or simpleuser@join.com were not found.");
            return;
        }

        var seeds = new List<(Guid UserId, bool IsSuperAdminTicket, bool CanFinishTicket, bool CanResolveTicket)>
        {
            (managerUser.Id, true, true, true),
            (simpleUser.Id, false, false, true)
        };

        var inserted = 0;
        var skipped = 0;
        var now = DateTime.UtcNow;

        foreach (var seed in seeds)
        {
            var existing = await _context.TicketUserCompanies
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x =>
                    x.CompanyId == joinCompanyId
                    && x.UserId == seed.UserId
                    && x.GcRecord == 0);

            if (existing is not null)
            {
                skipped++;
                continue;
            }

            _context.TicketUserCompanies.Add(new TicketUserCompany
            {
                CompanyId = joinCompanyId,
                UserId = seed.UserId,
                IsSuperAdminTicket = seed.IsSuperAdminTicket,
                CanFinishTicket = seed.CanFinishTicket,
                CanResolveTicket = seed.CanResolveTicket,
                Created = now,
                CreatedBy = "System_Seeder",
                GcRecord = 0
            });

            inserted++;
        }

        if (inserted > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("JOIN ticket roster seed finished. Inserted: {Inserted}, Skipped: {Skipped}", inserted, skipped);
    }

    /// <summary>
    /// Sembrado idempotente de la fila singleton de <see cref="TicketAttachmentSettings"/>
    /// para una empresa del seed de desarrollo. Sin esta fila, ningún upload
    /// funciona en el ambiente de desarrollo (respondería <c>ATTACHMENT_SETTINGS_NOT_CONFIGURED</c>).
    /// Los defaults siguen la spec: Pdf | Word | Excel | Text | Image, 10 MB,
    /// 10 archivos por ticket, sin límite diario.
    /// </summary>
    private async Task SeedTicketAttachmentSettingsAsync(Guid companyId)
    {
        var existing = await _context.TicketAttachmentSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.GcRecord == 0);

        if (existing is not null)
        {
            _logger.LogDebug("Ticket attachment settings already configured for company {CompanyId}; skipping.", companyId);
            return;
        }

        const int pdfWordExcelTextImage = (int)(JOIN.Domain.Enums.DocumentType.Pdf
            | JOIN.Domain.Enums.DocumentType.Word
            | JOIN.Domain.Enums.DocumentType.Excel
            | JOIN.Domain.Enums.DocumentType.Text
            | JOIN.Domain.Enums.DocumentType.Image);

        _context.TicketAttachmentSettings.Add(new TicketAttachmentSettings
        {
            CompanyId = companyId,
            AllowedDocumentTypes = (JOIN.Domain.Enums.DocumentType)pdfWordExcelTextImage,
            MaxFileSizeBytes = 10L * 1024 * 1024,
            MaxFilesPerTicket = 10,
            MaxFilesPerDay = null,
            Created = DateTime.UtcNow,
            CreatedBy = "System_Seeder",
            GcRecord = 0
        });

        await _context.SaveChangesAsync();
        _logger.LogInformation("Ticket attachment settings seeded for company {CompanyId}.", companyId);
    }

    private async Task SeedJoinTicketsAsync(Guid joinCompanyId)
    {
        var managerUser = await _context.ApplicationUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == "manager@join.com");

        var simpleUser = await _context.ApplicationUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == "simpleuser@join.com");

        if (managerUser is null || simpleUser is null)
        {
            _logger.LogWarning("Ticket seed skipped because default users manager@join.com and/or simpleuser@join.com were not found.");
            return;
        }

        var statusByCode = (await _context.TicketStatuses
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == joinCompanyId && x.GcRecord == 0)
            .OrderByDescending(x => x.LastModified ?? x.Created)
            .ToListAsync())
            .GroupBy(x => x.Code)
            .ToDictionary(g => g.Key, g => g.First().Id);

        // Catalogs are per tenant: without the CompanyId filter, IgnoreQueryFilters() would
        // hand JOIN tickets another company's complexity / time unit.
        var complexityByCode = (await _context.TicketComplexities
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == joinCompanyId && x.GcRecord == 0)
            .OrderByDescending(x => x.LastModified ?? x.Created)
            .ToListAsync())
            .GroupBy(x => x.Code)
            .ToDictionary(g => g.Key, g => g.First().Id);

        var defaultTimeUnit = await _context.TimeUnits
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == joinCompanyId && x.GcRecord == 0)
            .OrderBy(x => x.Code)
            .FirstOrDefaultAsync();

        var channel = await ResolveDefaultTicketChannelAsync();

        var customers = await _context.Persons
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == joinCompanyId && x.GcRecord == 0)
            .OrderBy(x => x.Created)
            .Take(3)
            .ToListAsync();

        var projects = await _context.Projects
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == joinCompanyId && x.GcRecord == 0)
            .OrderBy(x => x.Created)
            .Take(3)
            .ToListAsync();

        var areas = await _context.Areas
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == joinCompanyId && x.GcRecord == 0)
            .OrderBy(x => x.Created)
            .Take(3)
            .ToListAsync();

        if (!statusByCode.TryGetValue(1, out var openStatusId)
            || !statusByCode.TryGetValue(2, out var inProgressStatusId)
            || !statusByCode.TryGetValue(3, out var resolvedStatusId)
            || !complexityByCode.TryGetValue(1, out var lowComplexityId)
            || !complexityByCode.TryGetValue(2, out var mediumComplexityId)
            || !complexityByCode.TryGetValue(3, out var highComplexityId)
            || defaultTimeUnit is null
            || channel is null)
        {
            _logger.LogWarning("Ticket seed skipped because required ticket catalogs were not available.");
            return;
        }

        var now = DateTime.UtcNow;
        var codePrefix = $"TICK-{now:yyyyMM}-";
        var seeds = new List<TicketSeed>
        {
            new($"{codePrefix}0001", "Portal login intermittent failure", "Users report intermittent login failures in customer portal.", managerUser.Id, openStatusId, highComplexityId, 16m, 2m, true, customers.ElementAtOrDefault(0)?.Id, projects.ElementAtOrDefault(0)?.Id, areas.ElementAtOrDefault(0)?.Id),
            new($"{codePrefix}0002", "Invoice PDF formatting issue", "Generated invoice PDF truncates long customer names.", managerUser.Id, inProgressStatusId, mediumComplexityId, 8m, 3m, false, customers.ElementAtOrDefault(1)?.Id, projects.ElementAtOrDefault(1)?.Id, areas.ElementAtOrDefault(1)?.Id),
            new($"{codePrefix}0003", "Email notifications delayed", "Outbound email notifications are delayed more than 10 minutes.", managerUser.Id, openStatusId, mediumComplexityId, 12m, 1m, false, customers.ElementAtOrDefault(2)?.Id, projects.ElementAtOrDefault(2)?.Id, areas.ElementAtOrDefault(2)?.Id),
            new($"{codePrefix}0004", "Person merge validation", "Validation message should explain duplicate tax id behavior.", simpleUser.Id, resolvedStatusId, lowComplexityId, 4m, 4m, false, customers.ElementAtOrDefault(0)?.Id, projects.ElementAtOrDefault(0)?.Id, areas.ElementAtOrDefault(1)?.Id),
            new($"{codePrefix}0005", "Dashboard KPI discrepancy", "KPI cards display different totals than exports.", simpleUser.Id, inProgressStatusId, highComplexityId, 20m, 6m, false, customers.ElementAtOrDefault(1)?.Id, projects.ElementAtOrDefault(1)?.Id, areas.ElementAtOrDefault(2)?.Id),
            new($"{codePrefix}0006", "WhatsApp template selection", "Support agents need default template suggestion by ticket type.", simpleUser.Id, openStatusId, lowComplexityId, 6m, 0m, true, customers.ElementAtOrDefault(2)?.Id, projects.ElementAtOrDefault(2)?.Id, areas.ElementAtOrDefault(0)?.Id),
            new($"{codePrefix}0007", "Audit report export timeout", "Large audit report export times out after 90 seconds.", simpleUser.Id, openStatusId, highComplexityId, 24m, 5m, false, customers.ElementAtOrDefault(0)?.Id, projects.ElementAtOrDefault(0)?.Id, areas.ElementAtOrDefault(2)?.Id)
        };

        var existingCodes = await _context.Tickets
            .IgnoreQueryFilters()
            .Where(t => t.CompanyId == joinCompanyId)
            .Select(t => t.Code)
            .ToListAsync();

        var inserted = 0;

        foreach (var seed in seeds)
        {
            if (existingCodes.Contains(seed.Code, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var ticket = new Ticket
            {
                CompanyId = joinCompanyId,
                Name = seed.Name,
                Description = seed.Description,
                EstimatedTime = seed.EstimatedTime,
                ConsumedTime = seed.ConsumedTime,
                TimeUnitId = defaultTimeUnit.Id,
                TicketStatusId = seed.TicketStatusId,
                TicketComplexityId = seed.TicketComplexityId,
                PersonId = seed.PersonId,
                ProjectId = seed.ProjectId,
                AreaId = seed.AreaId,
                ChannelId = channel.Id,
                IsVisibleToExternals = seed.IsVisibleToExternals,
                CreatedByUserId = seed.AssignedToUserId,
                AssignedToUserId = seed.AssignedToUserId,
                Created = now,
                CreatedBy = "System_Seeder",
                GcRecord = 0
            };

            if (!int.TryParse(seed.Code.Split('-').Last(), out var seedSequence))
            {
                _logger.LogWarning("Ticket seed entry skipped because the sequence could not be extracted from code {Code}.", seed.Code);
                continue;
            }

            ticket.SetStandardCode(now.Year, now.Month, seedSequence);

            var seedCreationSummary = string.IsNullOrWhiteSpace(channel.Name)
                ? "Ticket creado"
                : $"Ticket creado desde {channel.Name}";

            ticket.AddLog(
                ticket.CreatedByUserId,
                LogType.Creation,
                seedCreationSummary);

            _context.Tickets.Add(ticket);

            inserted++;
        }

        if (inserted > 0)
        {
            await _context.SaveChangesAsync();
        }

        var seedCodes = seeds.Select(x => x.Code).ToList();
        var seededTickets = await _context.Tickets
            .IgnoreQueryFilters()
            .Where(t => t.CompanyId == joinCompanyId && t.GcRecord == 0 && seedCodes.Contains(t.Code))
            .ToListAsync();

        var ticketsWithLogs = await _context.TicketLogs
            .IgnoreQueryFilters()
            .Where(tl => tl.CompanyId == joinCompanyId && tl.GcRecord == 0)
            .Select(tl => tl.TicketId)
            .Distinct()
            .ToListAsync();

        var ticketIdsWithLogs = ticketsWithLogs.ToHashSet();
        var logsAdded = 0;

        foreach (var ticket in seededTickets)
        {
            if (ticketIdsWithLogs.Contains(ticket.Id))
            {
                continue;
            }

            // Tickets seeded before SPEC 99 keep their original channel, so only name the current default when it is theirs.
            var seedCreationSummary = ticket.ChannelId != channel.Id || string.IsNullOrWhiteSpace(channel.Name)
                ? "Ticket creado"
                : $"Ticket creado desde {channel.Name}";

            ticket.AddLog(
                ticket.CreatedByUserId,
                LogType.Creation,
                seedCreationSummary);

            logsAdded++;
        }

        if (logsAdded > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("JOIN tickets seed finished. Inserted: {Inserted}, Existing: {Existing}, LogsAdded: {LogsAdded}", inserted, seeds.Count - inserted, logsAdded);
    }

    /// <summary>
    /// SPEC 43: modules renamed to English. The seed finds a module by its previous name and updates that
    /// row in place (same Id), so <c>CompanyModules</c> and <c>SystemOptions</c> keep pointing at it.
    /// </summary>
    // ============================================================
    // CALENDAR MODULE (SPEC 44)
    // ============================================================

    private static readonly CalendarActivitySeed[] CalendarActivitySeeds =
    [
        new(CalendarCodes.Activities.Meeting, "Reunión", 60, AllowsOverlap: false, RequiresBusinessHours: true, "#1E88E5", 1),
        new(CalendarCodes.Activities.Appointment, "Cita", 60, AllowsOverlap: false, RequiresBusinessHours: true, "#43A047", 2),
        new(CalendarCodes.Activities.Task, "Tarea", 30, AllowsOverlap: true, RequiresBusinessHours: true, "#FB8C00", 3),
        new(CalendarCodes.Activities.Reminder, "Recordatorio", 15, AllowsOverlap: true, RequiresBusinessHours: false, "#8E24AA", 4),
        new(CalendarCodes.Activities.PersonReview, "Revisión de persona", 15, AllowsOverlap: true, RequiresBusinessHours: false, "#E53935", 5)
    ];

    private static readonly CalendarStatusSeed[] CalendarStatusSeeds =
    [
        new(CalendarCodes.Statuses.Pending, "Pendiente de confirmación", BlocksTime: true, IsFinal: false, "#FDD835", 1),
        new(CalendarCodes.Statuses.Confirmed, "Confirmada", BlocksTime: true, IsFinal: false, "#43A047", 2),
        new(CalendarCodes.Statuses.Rescheduled, "Reprogramada", BlocksTime: true, IsFinal: false, "#FB8C00", 3),
        new(CalendarCodes.Statuses.Cancelled, "Anulada", BlocksTime: false, IsFinal: true, "#9E9E9E", 4),
        new(CalendarCodes.Statuses.Completed, "Completada", BlocksTime: true, IsFinal: true, "#546E7A", 5)
    ];

    /// <summary>
    /// Seeds the global calendar catalogs (activity types and statuses), idempotent by <c>Code</c>. Rows that
    /// already exist are never overwritten: the SuperAdmin may have edited them.
    /// </summary>
    private async Task SeedCalendarGlobalCatalogsAsync()
    {
        var activityCodes = await _context.CalendarActivities
            .IgnoreQueryFilters()
            .Where(x => x.GcRecord == 0)
            .Select(x => x.Code)
            .ToListAsync();

        var statusCodes = await _context.CalendarStatuses
            .IgnoreQueryFilters()
            .Where(x => x.GcRecord == 0)
            .Select(x => x.Code)
            .ToListAsync();

        var inserted = 0;

        foreach (var seed in CalendarActivitySeeds.Where(seed => !activityCodes.Contains(seed.Code)))
        {
            _context.CalendarActivities.Add(new CalendarActivity
            {
                Code = seed.Code,
                Name = seed.Name,
                Color = seed.Color,
                DefaultDurationMinutes = seed.DurationMinutes,
                AllowsOverlap = seed.AllowsOverlap,
                RequiresBusinessHours = seed.RequiresBusinessHours,
                IsActive = true,
                SortOrder = seed.SortOrder,
                CreatedBy = "System_Seeder"
            });
            inserted++;
        }

        foreach (var seed in CalendarStatusSeeds.Where(seed => !statusCodes.Contains(seed.Code)))
        {
            _context.CalendarStatuses.Add(new CalendarStatus
            {
                Code = seed.Code,
                Name = seed.Name,
                Color = seed.Color,
                BlocksTime = seed.BlocksTime,
                IsFinal = seed.IsFinal,
                IsActive = true,
                SortOrder = seed.SortOrder,
                CreatedBy = "System_Seeder"
            });
            inserted++;
        }

        if (inserted > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("Calendar global catalogs seed finished. Inserted: {Inserted}", inserted);
    }

    /// <inheritdoc />
    public async Task<CalendarCatalogSyncResult> SeedCalendarCatalogsForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var globalStatuses = await _context.CalendarStatuses
            .IgnoreQueryFilters()
            .Where(x => x.GcRecord == 0 && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);

        var globalActivities = await _context.CalendarActivities
            .IgnoreQueryFilters()
            .Where(x => x.GcRecord == 0 && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);

        var companyStatuses = await _context.CalendarStatusCompanies
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.GcRecord == 0)
            .ToListAsync(cancellationToken);

        var companyActivityIds = await _context.CalendarActivityCompanies
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.GcRecord == 0)
            .Select(x => x.CalendarActivityId)
            .ToListAsync(cancellationToken);

        var statusesAdded = 0;
        foreach (var status in globalStatuses.Where(status => companyStatuses.All(x => x.CalendarStatusId != status.Id)))
        {
            var companyStatus = new CalendarStatusCompany
            {
                CompanyId = companyId,
                CalendarStatusId = status.Id,
                Name = status.Name,
                Color = status.Color,
                IsActive = true,
                SortOrder = status.SortOrder,
                CreatedBy = "System_Seeder"
            };
            _context.CalendarStatusCompanies.Add(companyStatus);
            companyStatuses.Add(companyStatus);
            statusesAdded++;
        }

        var pendingStatus = globalStatuses.FirstOrDefault(x => x.Code == CalendarCodes.Statuses.Pending);
        var pendingCompanyStatus = pendingStatus is null
            ? null
            : companyStatuses.FirstOrDefault(x => x.CalendarStatusId == pendingStatus.Id);

        var activitiesAdded = 0;
        var missingActivities = globalActivities.Where(activity => !companyActivityIds.Contains(activity.Id)).ToList();

        if (pendingCompanyStatus is null && missingActivities.Count > 0)
        {
            _logger.LogWarning(
                "Calendar activity types were not copied to company {CompanyId}: the global status {Code} is missing or inactive.",
                companyId,
                CalendarCodes.Statuses.Pending);
        }
        else
        {
            foreach (var activity in missingActivities)
            {
                _context.CalendarActivityCompanies.Add(new CalendarActivityCompany
                {
                    CompanyId = companyId,
                    CalendarActivityId = activity.Id,
                    Name = activity.Name,
                    Color = activity.Color,
                    DefaultDurationMinutes = activity.DefaultDurationMinutes,
                    AllowsOverlap = activity.AllowsOverlap,
                    RequiresBusinessHours = activity.RequiresBusinessHours,
                    InitialCalendarStatusCompanyId = pendingCompanyStatus!.Id,
                    IsActive = true,
                    SortOrder = activity.SortOrder,
                    CreatedBy = "System_Seeder"
                });
                activitiesAdded++;
            }
        }

        if (statusesAdded > 0 || activitiesAdded > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new CalendarCatalogSyncResult(activitiesAdded, statusesAdded);
    }

    /// <summary>
    /// Development data of the master company JOIN-001 (SPEC 44): the Calendar module enabled, its
    /// <see cref="CalendarCompany"/> (default user = the company's SuperAdmin, 08:00–17:00, America/Managua,
    /// reschedule status RESCHEDULED) and the default calendar of every seeded user. Idempotent.
    /// </summary>
    private async Task SeedJoinCalendarAsync(Guid joinCompanyId)
    {
        var calendarModuleId = await _context.SystemModules
            .IgnoreQueryFilters()
            .Where(m => m.Name == "Calendar" && m.GcRecord == 0)
            .Select(m => m.Id)
            .FirstOrDefaultAsync();

        if (calendarModuleId == Guid.Empty)
        {
            _logger.LogWarning("Calendar seed for JOIN-001 skipped: the Calendar system module was not found.");
            return;
        }

        var companyModule = await _context.CompanyModules
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(cm => cm.CompanyId == joinCompanyId && cm.ModuleId == calendarModuleId && cm.GcRecord == 0);

        if (companyModule is null)
        {
            _context.CompanyModules.Add(new CompanyModule
            {
                CompanyId = joinCompanyId,
                ModuleId = calendarModuleId,
                IsActive = true,
                CreatedBy = "System_Seeder"
            });
        }

        var seedEmails = GetDefaultUserSeeds().Select(x => x.Email).ToList();
        var seededUsers = await _context.ApplicationUsers
            .IgnoreQueryFilters()
            .Where(u => u.Email != null && seedEmails.Contains(u.Email) && u.GcRecord == 0)
            .ToListAsync();

        var defaultUser = seededUsers.FirstOrDefault(u => u.Email == "lcano@join.com")
            ?? seededUsers.FirstOrDefault(u => u.IsSuperAdminCompany);

        var calendarCompany = await _context.CalendarCompanies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.CompanyId == joinCompanyId && x.GcRecord == 0);

        if (calendarCompany is null)
        {
            var rescheduledStatusId = await _context.CalendarStatusCompanies
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == joinCompanyId
                    && x.GcRecord == 0
                    && x.CalendarStatus.Code == CalendarCodes.Statuses.Rescheduled)
                .Select(x => x.Id)
                .FirstOrDefaultAsync();

            if (defaultUser is null || rescheduledStatusId == Guid.Empty)
            {
                _logger.LogWarning("Calendar seed for JOIN-001 skipped: default user or RESCHEDULED status not found.");
                await _context.SaveChangesAsync();
                return;
            }

            calendarCompany = new CalendarCompany
            {
                CompanyId = joinCompanyId,
                DefaultActivityUserId = defaultUser.Id,
                TimeZone = "America/Managua",
                IsOpen24Hours = false,
                BusinessHoursStart = new TimeOnly(8, 0),
                BusinessHoursEnd = new TimeOnly(17, 0),
                DefaultCalendarName = "Mi calendario",
                RescheduledByOthersStatusCompanyId = rescheduledStatusId,
                CreatedBy = "System_Seeder"
            };
            _context.CalendarCompanies.Add(calendarCompany);
        }

        var memberIds = await _context.UserCompanies
            .IgnoreQueryFilters()
            .Where(uc => uc.CompanyId == joinCompanyId && uc.GcRecord == 0)
            .Select(uc => uc.UserId)
            .ToListAsync();

        var provisionedIds = await _context.CalendarUserConfigurations
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == joinCompanyId && x.GcRecord == 0)
            .Select(x => x.UserId)
            .ToListAsync();

        var provisioned = 0;
        foreach (var user in seededUsers.Where(u => memberIds.Contains(u.Id) && !provisionedIds.Contains(u.Id)))
        {
            var calendar = new UserCalendar
            {
                CompanyId = joinCompanyId,
                OwnerUserId = user.Id,
                Name = calendarCompany.DefaultCalendarName,
                Color = "#1E88E5",
                IsActive = true,
                SortOrder = 1,
                CreatedBy = "System_Seeder"
            };
            _context.UserCalendars.Add(calendar);
            _context.CalendarUserConfigurations.Add(new CalendarUserConfiguration
            {
                CompanyId = joinCompanyId,
                UserId = user.Id,
                DefaultCalendarId = calendar.Id,
                WorkDays = CalendarWorkDays.Weekdays,
                DefaultView = CalendarViewType.Week,
                WeekStartsOn = DayOfWeek.Monday,
                CreatedBy = "System_Seeder"
            });
            provisioned++;
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Calendar seed for JOIN-001 finished. Users provisioned: {Provisioned}", provisioned);
    }

    private sealed record CalendarActivitySeed(
        string Code, string Name, int DurationMinutes, bool AllowsOverlap, bool RequiresBusinessHours, string Color, int SortOrder);

    private sealed record CalendarStatusSeed(
        string Code, string Name, bool BlocksTime, bool IsFinal, string Color, int SortOrder);

    private static readonly IReadOnlyDictionary<string, string> LegacySystemModuleNames = new Dictionary<string, string>
    {
        ["Administracion"] = "Administration",
        ["Clientes"] = "Customers",
        ["Seguridad"] = "Security"
    };

    /// <summary>
    /// SPEC 43: module catalog (name, description, icon, base flag, order). Also read by
    /// <see cref="ComputeMenuPermissionsSeedChecksum"/> — do not duplicate this list elsewhere.
    /// </summary>
    private static List<SystemModuleSeed> GetSystemModuleSeeds() =>
    [
        new("Administration", "Administrative management module", "fa-solid fa-building", IsBase: true, Order: 1),
        new("Customers", "Customer management module", "fa-solid fa-users", IsBase: true, Order: 2),
        new("Tickets", "Ticket management module", "fa-solid fa-ticket", IsBase: true, Order: 3),
        // SPEC 44: optional module, enabled per company by the SuperAdmin.
        new("Calendar", "Calendar and activities module", "fa-solid fa-calendar-days", IsBase: false, Order: 4),
        new("Security", "Security module", "fa-solid fa-shield-halved", IsBase: true, Order: 99)
    ];

    private async Task SeedSystemModulesAsync()
    {
        var now = DateTime.UtcNow;
        var seeds = GetSystemModuleSeeds();
        var inserted = 0;
        var updated = 0;

        foreach (var seed in seeds)
        {
            var legacyNames = LegacySystemModuleNames
                .Where(pair => pair.Value == seed.Name)
                .Select(pair => pair.Key)
                .ToList();

            // The new name wins; the previous name is only used when the module was not renamed yet.
            var module = await _context.SystemModules
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(m => m.Name == seed.Name)
                ?? await _context.SystemModules
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(m => legacyNames.Contains(m.Name));

            if (module is null)
            {
                _context.SystemModules.Add(new SystemModule
                {
                    Name = seed.Name,
                    Description = seed.Description,
                    Icon = seed.Icon,
                    IsActive = true,
                    IsBase = seed.IsBase,
                    Order = seed.Order,
                    Created = now,
                    CreatedBy = "System_Seeder",
                    GcRecord = 0
                });
                inserted++;
                continue;
            }

            if (module.Name == seed.Name
                && module.Description == seed.Description
                && module.IsBase == seed.IsBase
                && module.Order == seed.Order)
            {
                continue;
            }

            module.Name = seed.Name;
            module.Description = seed.Description;
            module.IsBase = seed.IsBase;
            module.Order = seed.Order;
            module.LastModified = now;
            module.LastModifiedBy = "System_Seeder";
            updated++;
        }

        if (inserted > 0 || updated > 0)
        {
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation(
            "System modules catalog seed finished. Inserted: {Inserted}, Updated: {Updated}, Existing: {Existing}",
            inserted,
            updated,
            seeds.Count - inserted - updated);
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

        // SPEC 43: only BASE modules are assigned. Existing assignments are never deactivated or removed.
        var moduleIds = await _context.SystemModules
            .IgnoreQueryFilters()
            .Where(m => m.IsActive && m.IsBase && m.GcRecord == 0)
            .Select(m => m.Id)
            .ToListAsync();

        if (moduleIds.Count == 0)
        {
            _logger.LogWarning("Company modules seed skipped. No active base system modules were found.");
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
        // SPEC 43: renames run inside one transaction so a failure never leaves the menu half renamed.
        var ownsTransaction = _context.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await _context.Database.BeginTransactionAsync() : null;

        var modulesByName = await _context.SystemModules
            .IgnoreQueryFilters()
            .Where(m => m.GcRecord == 0)
            .ToDictionaryAsync(m => m.Name, m => m.Id, StringComparer.OrdinalIgnoreCase);

        var seeds = GetAdministrativeSystemOptionSeeds();

        // SPEC 43: all options, not only the ones of one module (they all used to hang from "Administracion").
        var existingOptions = await _context.SystemOptions
            .IgnoreQueryFilters()
            .ToListAsync();

        var optionsByRoute = existingOptions
            .Where(o => !string.IsNullOrWhiteSpace(o.Route))
            .GroupBy(o => o.Route!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var existingByName = existingOptions
            .GroupBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Options already matched to a seed in this run: each existing row belongs to exactly one seed.
        var claimed = new HashSet<Guid>();
        var seededByName = new Dictionary<string, SystemOption>(StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        var inserted = 0;
        var updated = 0;
        var skipped = 0;
        var menuOrder = 0;

        foreach (var seed in seeds.OrderBy(x => x.ParentName is null ? 0 : 1))
        {
            menuOrder += 10;

            if (!modulesByName.TryGetValue(seed.ModuleName, out var moduleId))
            {
                _logger.LogWarning(
                    "System option {OptionName} skipped: module {ModuleName} was not found.",
                    seed.Name,
                    seed.ModuleName);
                skipped++;
                continue;
            }

            Guid? parentId = null;
            if (!string.IsNullOrWhiteSpace(seed.ParentName) && seededByName.TryGetValue(seed.ParentName, out var parentOption))
            {
                parentId = parentOption.Id;
                if (parentOption.ModuleId != moduleId)
                {
                    _logger.LogWarning(
                        "System option {OptionName} declares module {ModuleName} but its parent {ParentName} belongs to another module.",
                        seed.Name,
                        seed.ModuleName,
                        seed.ParentName);
                }
            }

            var orderMenu = seed.OrderMenu ?? menuOrder;
            var canDownload = seed.CanDownload ?? seed.CanRead;
            var canExport = seed.CanExport ?? true;
            var canExecute = seed.CanExecute ?? true;
            var isVisibleMenu = seed.IsVisibleMenu ?? true;

            var option = FindExistingSystemOption(seed, optionsByRoute, existingByName, claimed);

            if (option is null)
            {
                option = new SystemOption
                {
                    ModuleId = moduleId,
                    Name = seed.Name,
                    Route = seed.Route,
                    Icon = seed.Icon,
                    ParentId = parentId,
                    ControllerName = seed.ControllerName,
                    CanRead = seed.CanRead,
                    CanCreate = seed.CanCreate,
                    CanUpdate = seed.CanUpdate,
                    CanDelete = seed.CanDelete,
                    CanDownload = canDownload,
                    CanExport = canExport,
                    CanExecute = canExecute,
                    IsVisibleMenu = isVisibleMenu,
                    OrderMenu = orderMenu,
                    Created = now,
                    CreatedBy = "System_Seeder",
                    GcRecord = 0
                };

                _context.SystemOptions.Add(option);
                claimed.Add(option.Id);
                seededByName[seed.Name] = option;
                inserted++;
                continue;
            }

            claimed.Add(option.Id);
            seededByName[seed.Name] = option;

            var hasChanges = option.Route != seed.Route
                || option.Name != seed.Name
                || option.ModuleId != moduleId
                || option.Icon != seed.Icon
                || option.ParentId != parentId
                || option.ControllerName != seed.ControllerName
                || option.CanRead != seed.CanRead
                || option.CanCreate != seed.CanCreate
                || option.CanUpdate != seed.CanUpdate
                || option.CanDelete != seed.CanDelete
                || option.CanDownload != canDownload
                || option.CanExport != canExport
                || option.CanExecute != canExecute
                || option.IsVisibleMenu != isVisibleMenu
                || option.OrderMenu != orderMenu
                || option.GcRecord != 0;

            if (!hasChanges)
            {
                continue;
            }

            option.Name = seed.Name;
            option.Route = seed.Route;
            option.ModuleId = moduleId;
            option.Icon = seed.Icon;
            option.ParentId = parentId;
            option.ControllerName = seed.ControllerName;
            option.CanRead = seed.CanRead;
            option.CanCreate = seed.CanCreate;
            option.CanUpdate = seed.CanUpdate;
            option.CanDelete = seed.CanDelete;
            option.CanDownload = canDownload;
            option.CanExport = canExport;
            option.CanExecute = canExecute;
            option.IsVisibleMenu = isVisibleMenu;
            option.OrderMenu = orderMenu;
            option.GcRecord = 0;
            option.LastModified = now;
            option.LastModifiedBy = "System_Seeder";
            updated++;
        }

        if (inserted > 0 || updated > 0)
        {
            await _context.SaveChangesAsync();
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }

        _logger.LogInformation(
            "System options seed finished. Inserted: {Inserted}, Updated: {Updated}, Skipped: {Skipped}, Existing: {Existing}",
            inserted,
            updated,
            skipped,
            seeds.Count - inserted - updated - skipped);
    }

    /// <summary>
    /// SPEC 43: finds the existing row of a seed, in this order: new route, previous route, new name, previous
    /// name. Routes are unique, so they decide the Persons/Clientes swap; rows already claimed by another seed
    /// in this run are skipped.
    /// </summary>
    private static SystemOption? FindExistingSystemOption(
        SystemOptionSeed seed,
        IReadOnlyDictionary<string, SystemOption> optionsByRoute,
        IReadOnlyDictionary<string, SystemOption> optionsByName,
        IReadOnlySet<Guid> claimed)
    {
        SystemOption? Unclaimed(SystemOption? candidate) =>
            candidate is not null && !claimed.Contains(candidate.Id) ? candidate : null;

        if (Unclaimed(optionsByRoute.GetValueOrDefault(seed.Route)) is { } byRoute)
        {
            return byRoute;
        }

        foreach (var legacyRoute in LegacySystemOptionRoutes.Where(pair => pair.Value == seed.Name).Select(pair => pair.Key))
        {
            if (Unclaimed(optionsByRoute.GetValueOrDefault(legacyRoute)) is { } byLegacyRoute)
            {
                return byLegacyRoute;
            }
        }

        if (Unclaimed(optionsByName.GetValueOrDefault(seed.Name)) is { } byName)
        {
            return byName;
        }

        foreach (var legacyName in LegacySystemOptionNames.Where(pair => pair.Value == seed.Name).Select(pair => pair.Key))
        {
            if (Unclaimed(optionsByName.GetValueOrDefault(legacyName)) is { } byLegacyName)
            {
                return byLegacyName;
            }
        }

        return null;
    }

    private async Task SeedRoleSystemOptionsAsync(Guid joinCompanyId)
    {
        var baseSeeds = GetRoleSystemOptionSeeds();
        var roleNames = baseSeeds
            .Select(x => x.RoleName)
            .Concat(PrivilegedRoleNames)
            .Distinct()
            .ToList();

        var roles = await _context.ApplicationRoles
            .IgnoreQueryFilters()
            .Where(r => r.Name != null && roleNames.Contains(r.Name))
            .ToListAsync();

        var systemOptions = await _context.SystemOptions
            .IgnoreQueryFilters()
            .Where(o => o.GcRecord == 0)
            .ToListAsync();

        var privilegedAllOptionSeeds = systemOptions
            .SelectMany(option => PrivilegedRoleNames.Select(roleName => new RoleSystemOptionSeed(
                roleName,
                option.Name,
                true,
                true,
                true,
                true,
                CanDownload: true,
                CanExport: true,
                CanExecute: true,
                IsVisibleMenu: true,
                OrderMenu: option.OrderMenu)))
            .ToList();

        var seeds = baseSeeds
            .Concat(GetAdminFullSystemOptionPermissionSeeds())
            .Concat(privilegedAllOptionSeeds)
            .GroupBy(x => $"{x.RoleName}|{x.SystemOptionName}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();

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

            var canDownload = seed.CanDownload ?? seed.CanRead;
            var canExport = seed.CanExport ?? seed.CanRead;
            var canExecute = seed.CanExecute ?? seed.CanRead;
            var isVisibleMenu = seed.IsVisibleMenu ?? seed.CanRead;

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
                    CanDownload = canDownload,
                    CanExport = canExport,
                    CanExecute = canExecute,
                    IsVisibleMenu = isVisibleMenu,
                    OrderMenu = seed.OrderMenu,
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
                || permission.CanDownload != canDownload
                || permission.CanExport != canExport
                || permission.CanExecute != canExecute
                || permission.IsVisibleMenu != isVisibleMenu
                || permission.OrderMenu != seed.OrderMenu
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
            permission.CanDownload = canDownload;
            permission.CanExport = canExport;
            permission.CanExecute = canExecute;
            permission.IsVisibleMenu = isVisibleMenu;
            permission.OrderMenu = seed.OrderMenu;
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
        new("Livingstone23", "Admin", "livingstone23@gmail.com", "ABCabc123*", "Admin", false, true),
        new("Manager", "Test", "manager@join.com", "ABCabc123*", "Manager", false),
        new("Supervisor", "Test", "supervisor@join.com", "ABCabc123*", "Supervisor", false),
        new("SimpleUser", "Test", "simpleuser@join.com", "ABCabc123*", "UsuarioSimple", false),
        // SPEC 45 — service user for the channel agent.
        new("Agent", "Dev", "agent@join.com", "ABCabc123*", "Agent", false)
    ];

    private static List<RoleSystemOptionSeed> GetAdminFullSystemOptionPermissionSeeds()
    {
        var optionNames = GetAdministrativeSystemOptionSeeds()
            .Select(x => x.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return optionNames
            .Select((optionName, index) => new RoleSystemOptionSeed(
                "Admin",
                optionName,
                true,
                true,
                true,
                true,
                CanDownload: true,
                CanExport: true,
                CanExecute: true,
                IsVisibleMenu: true,
                OrderMenu: index + 1))
            .ToList();
    }

    /// <summary>
    /// SPEC 43: options renamed to English. Previous name → new name. An existing option is recognized by its
    /// new or previous route first (routes are unique), then by its new or previous name, and updated in place
    /// (same Id, so its <c>RoleSystemOptions</c> are kept). Order matters for the Persons/Clientes swap: the
    /// parent <c>Persons</c> (/Clientes) becomes <c>CustomersMenu</c> and the child <c>Clientes</c> becomes <c>Persons</c>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> LegacySystemOptionNames = new Dictionary<string, string>
    {
        ["Administracion"] = "Administration",
        ["Paises"] = "Countries",
        ["Genero"] = "Genders",
        ["Industria"] = "Industries",
        ["Rango Ingreso"] = "IncomeRanges",
        ["Regimen Tributario"] = "TaxRegimes",
        ["Persons"] = "CustomersMenu",
        ["Clientes"] = "Persons",
        ["ManejoTickets"] = "TicketManagement",
        ["Seguridad"] = "Security",
        ["Usuarios"] = "Users",
        ["Compañias"] = "Companies",
        ["SystemOption"] = "SystemOptions",
        ["RoleSystemOption"] = "RoleSystemOptions",
        ["Bitacora"] = "AuditLog"
    };

    /// <summary>
    /// SPEC 43: previous route → new option name. Used before any name lookup, so each existing row is claimed
    /// by exactly one seed (see <see cref="LegacySystemOptionNames"/>).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> LegacySystemOptionRoutes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["/administracion"] = "Administration",
        ["/administracion/countries"] = "Countries",
        ["/administracion/regions"] = "Regions",
        ["/administracion/provinces"] = "Provinces",
        ["/administracion/municipalities"] = "Municipalities",
        ["/administracion/identification-types"] = "IdentificationTypes",
        ["/administracion/Street-types"] = "StreetTypes",
        ["/administracion/areas"] = "Areas",
        ["/administracion/projects"] = "Projects",
        ["/administracion/system-modules"] = "SystemModules",
        ["/administracion/entity-statuses"] = "EntityStatuses",
        ["/administracion/company-modules"] = "CompanyModules",
        ["/administracion/communication-channels"] = "CommunicationChannels",
        ["/administracion/gender"] = "Genders",
        ["/administracion/industry"] = "Industries",
        ["/administracion/incomerange"] = "IncomeRanges",
        ["/administracion/taxregime"] = "TaxRegimes",
        ["/Clientes"] = "CustomersMenu",
        ["/Clientes/Cliente"] = "Persons",
        ["/Clientes/customers"] = "Customers",
        ["/ManejoTickets"] = "TicketManagement",
        ["/ManejoTickets/tickets"] = "Tickets",
        ["/ManejoTickets/time-units"] = "TimeUnits",
        ["/ManejoTickets/ticket-complexities"] = "TicketComplexities",
        ["/ManejoTickets/ticket-statuses"] = "TicketStatuses",
        ["/ManejoTickets/ticket-company-defaults"] = "TicketCompanyDefaults",
        ["/ManejoTickets/ticket-user-companies"] = "TicketUserCompanies",
        ["/ManejoTickets/ticket-attachment-settings"] = "TicketAttachmentSettings",
        ["/ManejoTickets/ticket-documents"] = "TicketDocuments",
        ["/ManejoTickets/ticket-status-transitions"] = "TicketStatusTransitions"
    };

    // SPEC 43: every option declares its real module (ModuleName); names and routes in English with hyphens.
    // ControllerName never changes: it is the permission resource ([PermissionResource], PermissionService).
    private static List<SystemOptionSeed> GetAdministrativeSystemOptionSeeds() =>
    [
        new("Administration", "/administration", "@Icons.Material.Filled.AdminPanelSettings", "Administration", null, null, false, false, false, false),
        new("Countries", "/administration/countries", "@Icons.Material.Filled.AddLocationAlt", "Administration", "Administration", "Countries", true, true, true, true),
        new("Regions", "/administration/regions", "@Icons.Material.Filled.AddLocationAlt", "Administration", "Administration", "Regions", true, true, true, true),
        new("Provinces", "/administration/provinces", "@Icons.Material.Filled.AddLocationAlt", "Administration", "Administration", "Provinces", true, true, true, true),
        new("Municipalities", "/administration/municipalities", "@Icons.Material.Filled.AddLocationAlt", "Administration", "Administration", "Municipalities", true, true, true, true),
        new("IdentificationTypes", "/administration/identification-types", "@Icons.Material.Filled.AddCard", "Administration", "Administration", "IdentificationTypes", true, true, true, true),

        new("StreetTypes", "/administration/street-types", "@Icons.Material.Filled.AddCard", "Administration", "Administration", "StreetTypes", true, true, true, true),

        new("Areas", "/administration/areas", "@Icons.Material.Filled.Extension", "Administration", "Administration", "Areas", true, true, true, true),
        new("Projects", "/administration/projects", "@Icons.Material.Filled.Factory", "Administration", "Administration", "Projects", true, true, true, true),
        new("SystemModules", "/administration/system-modules", "@Icons.Material.Filled.FolderCopy", "Administration", "Administration", "SystemModules", true, true, true, true),
        new("EntityStatuses", "/administration/entity-statuses", "@Icons.Material.Filled.Highlight", "Administration", "Administration", "EntityStatuses", true, true, true, true),
        new("CompanyModules", "/administration/company-modules", "@Icons.Material.Filled.HolidayVillage", "Administration", "Administration", "CompanyModules", true, true, true, true),
        new("CommunicationChannels", "/administration/communication-channels", "@Icons.Material.Filled.KeyboardAlt", "Administration", "Administration", "CommunicationChannels", true, true, true, true),


        new("Genders", "/administration/genders", "@Icons.Material.Filled.Factory", "Administration", "Administration", "Genders", true, true, true, true),
        new("Industries", "/administration/industries", "@Icons.Material.Filled.Factory", "Administration", "Administration", "Industries", true, true, true, true),
        new("IncomeRanges", "/administration/income-ranges", "@Icons.Material.Filled.Factory", "Administration", "Administration", "IncomeRanges", true, true, true, true),
        new("TaxRegimes", "/administration/tax-regimes", "@Icons.Material.Filled.Factory", "Administration", "Administration", "TaxRegimes", true, true, true, true),


        // SPEC 43: the parent was "Persons" (/Clientes); it is renamed CustomersMenu to free the name Persons
        // for the persons screen, whose permission resource is "Persons".
        new("CustomersMenu", "/customers", "@Icons.Material.Filled.PermContactCalendar", "Customers", null, null, false, false, false, false),
        new("Persons", "/customers/persons", "@Icons.Material.Filled.PeopleAlt", "Customers", "CustomersMenu", "Persons", true, true, true, true),
        new("Customers", "/customers/customers", "@Icons.Material.Filled.Badge", "Customers", "CustomersMenu", "Customers", true, true, true, true, CanDownload: true, IsVisibleMenu: true, OrderMenu: 15),


        new("TicketManagement", "/tickets", "@Icons.Material.Filled.Label", "Tickets", null, null, false, false, false, false),
        new("Tickets", "/tickets/tickets", "@Icons.Material.Filled.LocalPlay", "Tickets", "TicketManagement", "Tickets", false, false, false, false),
        new("TimeUnits", "/tickets/time-units", "@Icons.Material.Filled.MoreTime", "Tickets", "TicketManagement", "TimeUnits", true, true, true, true),
        new("TicketComplexities", "/tickets/ticket-complexities", "@Icons.Material.Filled.Fluorescent", "Tickets", "TicketManagement", "TicketComplexities", true, true, true, true),
        new("TicketStatuses", "/tickets/ticket-statuses", "@Icons.Material.Filled.PendingActions", "Tickets", "TicketManagement", "TicketStatuses", true, true, true, true),
        new("TicketCompanyDefaults", "/tickets/ticket-company-defaults", "@Icons.Material.Filled.LocalPlay", "Tickets", "TicketManagement", "TicketCompanyDefaults", true, true, true, true),
        // SPEC 34 — roster de agentes de tickets por empresa.
        new("TicketUserCompanies", "/tickets/ticket-user-companies", "@Icons.Material.Filled.SupervisorAccount", "Tickets", "TicketManagement", "TicketUserCompanies", true, true, true, true),

        // SPEC 36 — attachments de tickets. TicketAttachmentSettings NO usa CanDownload
        // (no tiene endpoint de descarga propio); TicketDocuments sí, con CanDownload explícito
        // porque es el único recurso del sistema donde el flag se usa de verdad vía
        // [RequirePermission(PermissionFlags.CanDownload)] en el endpoint de descarga.
        new("TicketAttachmentSettings", "/tickets/ticket-attachment-settings", "@Icons.Material.Filled.SettingsApplications", "Tickets", "TicketManagement", "TicketAttachmentSettings", true, true, true, true),
        new("TicketDocuments", "/tickets/ticket-documents", "@Icons.Material.Filled.AttachFile", "Tickets", "TicketManagement", "TicketDocuments", true, true, true, true, CanDownload: true),

        // SPEC 37 — transiciones de status parametrizables (sub-recurso de configuración de Ticket).
        // CanUpdate queda en false por diseño: el par (FromStatusId, ToStatusId) es la identidad
        // de la regla, no un dato editable — el endpoint "Update" no existe (ver Scope).
        new("TicketStatusTransitions", "/tickets/ticket-status-transitions", "@Icons.Material.Filled.SyncAlt", "Tickets", "TicketManagement", "TicketStatusTransitions", true, true, false, true),

        // SPEC 43: the security options hang from the "Security" parent (they used to sit at the menu root).
        new("Security", "/security", "@Icons.Material.Filled.Security", "Security", null, null, false, false, false, false),
        new("Users", "/security/users", "@Icons.Material.Filled.SupervisedUserCircle", "Security", "Security", "Users", false, false, false, false),
        new("Roles", "/security/roles", "@Icons.Material.Filled.VerifiedUser", "Security", "Security", "Roles", false, false, false, false),
        // Reubicado desde Administration a Security (join_frontb specs/10-role-companies.md); el
        // recurso de permiso sigue siendo "Companies" y el CompaniesController no se mueve.
        new("Companies", "/security/companies", "@Icons.Material.Filled.HomeWork", "Security", "Security", "Companies", true, true, true, true),
        // "RoleCompanies" no tiene todavía una pantalla frontend confirmada.
        new("RoleCompanies", "/security/role-companies", "@Icons.Material.Filled.VerifiedUser", "Security", "Security", "RoleCompanies", false, false, false, false),
        // Pantalla /security/system-options (join_frontb specs/11).
        new("SystemOptions", "/security/system-options", "@Icons.Material.Filled.SystemSecurityUpdateWarning", "Security", "Security", "SystemOptions", false, false, false, false),
        // Respalda la matriz de /security/roles (specs/09); el recurso de permiso sigue siendo "RoleSystemOption".
        new("RoleSystemOptions", "/security/role-system-options", "@Icons.Material.Filled.SystemSecurityUpdateGood", "Security", "Security", "RoleSystemOption", false, false, false, false),
        // SPEC 29 — bitácora de seguridad. Read-only; other flags left at false so the
        // DynamicAuthorizationFilter denies anything other than GET. Without this row
        // the AuditController would 403 for everyone except SuperAdmin.
        new("AuditLog", "/security/audit-log", "@Icons.Material.Filled.HistoryEdu", "Security", "Security", "Audit", true, false, false, false),

        // SPEC 44 — calendar module. Option names are definitive for the frontend; ControllerName is the resource.
        new("Calendar", "/calendar", "@Icons.Material.Filled.CalendarMonth", "Calendar", null, null, false, false, false, false),
        new("MyCalendar", "/calendar/my-calendar", "@Icons.Material.Filled.Today", "Calendar", "Calendar", "Calendar", true, true, true, true),
        new("CalendarPanel", "/calendar/panel", "@Icons.Material.Filled.ViewTimeline", "Calendar", "Calendar", "CalendarOthers", true, true, true, true),
        new("ActivityConfirmation", "/calendar/activity-confirmation", "@Icons.Material.Filled.EventAvailable", "Calendar", "Calendar", "CalendarActivityConfirmation", false, false, true, false, IsVisibleMenu: false),
        new("CalendarSettings", "/calendar/settings", "@Icons.Material.Filled.EditCalendar", "Calendar", "Calendar", "CalendarSettings", true, true, true, true, CanExecute: true),
        new("CalendarCatalogs", "/calendar/catalogs", "@Icons.Material.Filled.Category", "Calendar", "Calendar", "CalendarCatalogs", true, true, true, true),

        // SPEC 47 — endpoint para gestionar canales de ingesta inbound de tickets (Twilio WhatsApp, SendGrid email).
        // CanUpdate queda en false por diseño: rotar un token de webhook es soft-delete + create, no update in-place.
        new("TicketInboundChannels", "/tickets/ticket-inbound-channels", "@Icons.Material.Filled.Webhook", "Tickets", "TicketManagement", "TicketInboundChannels", true, true, false, true),

        // SPEC 45 — channel agent intake (no menu) and pending-persons inbox.
        new("CalendarChannelIntake", "/calendar/channel-intake", "@Icons.Material.Filled.SmartToy", "Calendar", "Calendar", "CalendarChannelIntake", true, true, true, false, IsVisibleMenu: false),
        new("PendingPersons", "/customers/pending-persons", "@Icons.Material.Filled.PersonSearch", "Customers", "CustomersMenu", "PersonPendingConfirmations", true, false, true, false)

    ];

    private static List<RoleSystemOptionSeed> GetRoleSystemOptionSeeds() =>
    [
        new("SuperAdmin", "SystemModules", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("SuperAdmin", "TimeUnits", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("SuperAdmin", "TicketComplexities", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("SuperAdmin", "TicketStatuses", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("SuperAdmin", "TicketCompanyDefaults", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("SuperAdmin", "TicketUserCompanies", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("SuperAdmin", "Customers", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("SuperAdmin", "Companies", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),


        new("Admin", "Administration", false, false, false, false, CanDownload: true, CanExport: true, CanExecute: true),
        new("Admin", "TimeUnits", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Admin", "TicketComplexities", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Admin", "TicketStatuses", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Admin", "TicketCompanyDefaults", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Admin", "TicketUserCompanies", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Admin", "Customers", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),

        new("Manager", "Administration", false, false, false, false, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "Countries", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "Regions", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "Provinces", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "Municipalities", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "IdentificationTypes", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "StreetTypes", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "Areas", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "Projects", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "Genders", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "Industries", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "IncomeRanges", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "TaxRegimes", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "EntityStatuses", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "CompanyModules", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "CommunicationChannels", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),

        new("Manager", "CustomersMenu", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "Persons", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "Customers", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),

        new("Manager", "Companies", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "TicketManagement", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "Tickets", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "TimeUnits", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "TicketComplexities", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "TicketStatuses", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "TicketCompanyDefaults", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "TicketUserCompanies", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        // SPEC 37 — Manager gestiona transiciones (CanExecute para escenarios donde aplique).
        new("Manager", "TicketStatusTransitions", true, true, false, true, CanDownload: false, CanExport: false, CanExecute: true),
        new("Manager", "Security", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "Users", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "Roles", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "SystemOptions", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "RoleSystemOptions", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),


        new("Supervisor", "Administration", false, false, false, false),
        new("Supervisor", "Countries", true, true, true, false),
        new("Supervisor", "Regions", true, true, true, false),
        new("Supervisor", "Provinces", true, true, true, false),
        new("Supervisor", "Municipalities", true, true, true, false),
        new("Supervisor", "IdentificationTypes", true, true, true, false),
        new("Supervisor", "StreetTypes", true, true, true, true),
        new("Supervisor", "Areas", true, true, true, false),
        new("Supervisor", "Projects", true, true, true, false),
        new("Supervisor", "Genders", true, true, true, false),
        new("Supervisor", "Industries", true, true, true, false),
        new("Supervisor", "IncomeRanges", true, true, true, false),
        new("Supervisor", "TaxRegimes", true, true, true, false),
        new("Supervisor", "EntityStatuses", true, true, true, false),
        new("Supervisor", "CompanyModules", true, true, true, false),
        new("Supervisor", "CustomersMenu", true, true, true, true),
        new("Supervisor", "Persons", true, true, true, true),
        new("Supervisor", "Customers", true, true, true, false),
        new("Supervisor", "CommunicationChannels", true, true, true, false),
        new("Supervisor", "Companies", true, true, true, false),
        new("Supervisor", "TicketManagement", true, true, true, true),
        new("Supervisor", "Tickets", true, true, true, true),
        new("Supervisor", "TimeUnits", true, true, true, false),
        new("Supervisor", "TicketComplexities", true, true, true, false),
        new("Supervisor", "TicketStatuses", true, true, true, false),
        new("Supervisor", "TicketUserCompanies", true, false, false, false),
        // SPEC 37 — Supervisor solo lee el catálogo de transiciones configuradas.
        new("Supervisor", "TicketStatusTransitions", true, false, false, false),

        // `UsuarioSimple` receives read-only access to selected administrative options while the remaining entries stay restricted by default.
        new("UsuarioSimple", "Administration", false, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "Countries", false, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "Regions", false, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "Provinces", false, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "Municipalities", false, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "IdentificationTypes", true, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "StreetTypes", true, true, true, true, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "Areas", true, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "Projects", true, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "Genders", true, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "Industries", true, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "IncomeRanges", true, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "TaxRegimes", true, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "EntityStatuses", true, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "CompanyModules", true, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "CommunicationChannels", false, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "Companies", false, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "TicketManagement", true, true, true, true, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "Tickets", true, true, true, true, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "TimeUnits", false, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "TicketComplexities", true, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "TicketStatuses", true, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "TicketUserCompanies", false, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        // SPEC 37 — UsuarioSimple solo lee, sin Download/Export/Execute.
        new("UsuarioSimple", "TicketStatusTransitions", true, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),

        // SPEC 47 — canales de ingesta inbound (Twilio WhatsApp, SendGrid email). CanUpdate=false
        // por diseño: rotar es delete + create. Admin y SuperAdminCompany reciben acceso total vía
        // PrivilegedRoleNames; Agent y Person no tienen fila (denegado por defecto).
        new("SuperAdmin", "TicketInboundChannels", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "TicketInboundChannels", true, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("Supervisor", "TicketInboundChannels", true, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("Coordinador", "TicketInboundChannels", true, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "TicketInboundChannels", true, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),

        // SPEC 36 — adjuntos de tickets por rol. TicketAttachmentSettings es la config
        // singleton por empresa (no endpoint de descarga propio); TicketDocuments es
        // el recurso donde CanDownload se usa de verdad.
        new("Manager", "TicketAttachmentSettings", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Manager", "TicketDocuments", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
        new("Supervisor", "TicketAttachmentSettings", true, false, false, false),
        new("Supervisor", "TicketDocuments", true, true, false, false, CanDownload: true),
        new("UsuarioSimple", "TicketAttachmentSettings", false, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
        new("UsuarioSimple", "TicketDocuments", true, true, false, false, CanDownload: true, CanExport: false, CanExecute: false),

        // SPEC 44 — calendar. Rows reference the OPTION name (MyCalendar → resource Calendar, CalendarPanel →
        // CalendarOthers, ActivityConfirmation → CalendarActivityConfirmation). Admin and SuperAdminCompany get
        // full access through PrivilegedRoleNames; Agent and Person get nothing here (Agent: SPEC 45).
        new("SuperAdmin", "MyCalendar", true, true, true, true),
        new("SuperAdmin", "CalendarPanel", true, true, true, true),
        new("SuperAdmin", "ActivityConfirmation", false, false, true, false),
        new("SuperAdmin", "CalendarSettings", true, true, true, true, CanExecute: true),
        new("SuperAdmin", "CalendarCatalogs", true, true, true, true),
        new("Manager", "MyCalendar", true, true, true, true),
        new("Manager", "CalendarPanel", true, true, true, true),
        new("Manager", "ActivityConfirmation", false, false, true, false),
        new("Manager", "CalendarSettings", true, true, true, false),
        new("Supervisor", "MyCalendar", true, true, true, true),
        new("Supervisor", "CalendarPanel", true, true, true, false),
        new("Supervisor", "ActivityConfirmation", false, false, true, false),
        new("Supervisor", "CalendarSettings", true, false, false, false),
        new("Coordinador", "MyCalendar", true, true, true, true),
        new("Coordinador", "CalendarPanel", true, false, false, false),
        new("Coordinador", "ActivityConfirmation", false, false, true, false),
        new("UsuarioSimple", "MyCalendar", true, true, true, true),
        new("UsuarioSimple", "ActivityConfirmation", false, false, true, false),

        // SPEC 45 — channel agent (CalendarChannelIntake, no CanDelete) and pending-persons inbox (read + update).
        // Admin and SuperAdminCompany get full access through PrivilegedRoleNames; the agent gets nothing else.
        new("SuperAdmin", "CalendarChannelIntake", true, true, true, false),
        new("SuperAdmin", "PendingPersons", true, false, true, false),
        new("Agent", "CalendarChannelIntake", true, true, true, false),
        new("Manager", "PendingPersons", true, false, true, false),
        new("Supervisor", "PendingPersons", true, false, true, false)

    ];

    private sealed record CountrySeed(string Name, string IsoCode);
    private sealed record ProvinceSeed(string CountryIsoCode, string Name, string Code);
    private sealed record MunicipalitySeed(string CountryIsoCode, string ProvinceCode, string Name, string? Code = null);
    private sealed record StreetTypeSeed(string Name, string Abbreviation);
    private static readonly HashSet<string> FemaleSeedFirstNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ana", "Maria", "Sandra", "Claudia", "Patricia", "Carmen", "Silvia", "Diana", "Valeria", "Natalia",
        "Beatriz", "Lucia", "Monica", "Sarah", "Emily",
        "Gabriela", "Isabel", "Teresa", "Rosa", "Cristina", "Adriana", "Yolanda", "Elena", "Gloria", "Marta",
        "Rocio", "Susana", "Karla", "Vanessa", "Ximena", "Daniela", "Fabiola", "Norma"
    };

    private async Task<Dictionary<string, Guid>> GetGenderIdsByNameAsync(Guid companyId) =>
        await _context.Genders
            .IgnoreQueryFilters()
            .Where(g => g.CompanyId == companyId && g.GcRecord == 0)
            .ToDictionaryAsync(g => g.Name, g => g.Id, StringComparer.OrdinalIgnoreCase);

    private static Guid? ResolveGenderId(string firstName, IReadOnlyDictionary<string, Guid> gendersByName)
    {
        if (!gendersByName.TryGetValue("Masculino", out var maleId) ||
            !gendersByName.TryGetValue("Femenino", out var femaleId))
        {
            return null;
        }

        return FemaleSeedFirstNames.Contains(firstName.Trim()) ? femaleId : maleId;
    }

    private sealed record PersonSeed(
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
        bool IsSuperAdmin,
        bool IsSuperAdminCompany = false);
    private sealed record SystemModuleSeed(
        string Name,
        string Description,
        string Icon,
        bool IsBase,
        int Order);
    private sealed record SystemOptionSeed(
        string Name,
        string Route,
        string? Icon,
        string ModuleName,
        string? ParentName,
        string? ControllerName,
        bool CanRead,
        bool CanCreate,
        bool CanUpdate,
        bool CanDelete,
        bool? CanDownload = null,
        bool? CanExport = null,
        bool? CanExecute = null,
        bool? IsVisibleMenu = null,
        int? OrderMenu = null);
    private sealed record RoleSystemOptionSeed(
        string RoleName,
        string SystemOptionName,
        bool CanRead,
        bool CanCreate,
        bool CanUpdate,
        bool CanDelete,
        bool? CanDownload = null,
        bool? CanExport = null,
        bool? CanExecute = null,
        bool? IsVisibleMenu = null,
        int? OrderMenu = null);

    private sealed record TicketSeed(
        string Code,
        string Name,
        string Description,
        Guid AssignedToUserId,
        Guid TicketStatusId,
        Guid TicketComplexityId,
        decimal EstimatedTime,
        decimal ConsumedTime,
        bool IsVisibleToExternals,
        Guid? PersonId,
        Guid? ProjectId,
        Guid? AreaId);
}



