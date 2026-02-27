using System.Security.Claims;
using JOIN.Domain.Security;
using JOIN.Infrastructure.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JOIN.Infrastructure.Identity;

/// <summary>
/// Custom factory to inject tenant-specific roles into the user's JWT or Cookie claims.
/// </summary>
public class CustomUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>
{
    // Required to query the user's roles per company/tenant
    private readonly ApplicationDbContext _dbContext; 

    public CustomUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor,
        ApplicationDbContext dbContext)
        : base(userManager, roleManager, optionsAccessor)
    {
        _dbContext = dbContext;
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        // 1. Generate basic Identity claims (Id, UserName, Email, etc.)
        var identity = await base.GenerateClaimsAsync(user);

        // 2. Retrieve the user's active roles mapped by company
        var userRolesByCompany = await _dbContext.Set<UserRoleCompany>()
            .Include(urc => urc.Role)
            .Where(urc => urc.UserId == user.Id && urc.GcRecord == 0)
            .ToListAsync();

        // 3. Inject roles into the token using a structured format (e.g., "Role_CompanyId").
        // This ensures that during authorization, you can validate if the user has 
        // the "Admin" claim specifically for the "<Current_CompanyId>".
        foreach (var urc in userRolesByCompany)
        {
            if (urc.Role?.Name != null)
            {
                // Custom format: "RoleName_CompanyId"
                string tenantRoleClaim = $"{urc.Role.Name}_{urc.CompanyId}";
                identity.AddClaim(new Claim("TenantRole", tenantRoleClaim));
            }
        }

        // 4. (Optional) Add the user's current or default company if applicable
        var defaultCompany = await _dbContext.Set<UserCompany>()
            .Where(uc => uc.UserId == user.Id && uc.GcRecord == 0)
            .Select(uc => uc.CompanyId)
            .FirstOrDefaultAsync();

        if (defaultCompany != Guid.Empty)
        {
            identity.AddClaim(new Claim("DefaultCompanyId", defaultCompany.ToString()));
        }

        return identity;
    }
}
