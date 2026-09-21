using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;



namespace JOIN.Application.UseCases.Security.Auth.Login;



/// <summary>
/// Resolves the effective company/roles for an already-authenticated user and issues the resulting
/// session (refresh token + JWT). Shared by <see cref="LoginCommandHandler"/> (no 2FA configured) and
/// the MFA challenge verification handler (2FA configured, code already validated) so the resolution
/// logic is never duplicated between them.
/// </summary>
/// <param name="userManager">ASP.NET Core Identity manager used to resolve the SuperAdmin role membership.</param>
/// <param name="jwtTokenGenerator">Token generator used to create the JWT returned to the client.</param>
/// <param name="unitOfWork">Unit of work used to query tenant memberships and role assignments.</param>
/// <param name="currentUserService">Resolves the caller's IP/user-agent for audit logging.</param>
/// <param name="securityEventLogger">Records the <c>LoginSucceeded</c> security event.</param>
public class AuthenticatedSessionIssuer(
    UserManager<ApplicationUser> userManager,
    IJwtTokenGenerator jwtTokenGenerator,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ISecurityEventLogger securityEventLogger)
    : IAuthenticatedSessionIssuer
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly IJwtTokenGenerator _jwtTokenGenerator = jwtTokenGenerator;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly ISecurityEventLogger _securityEventLogger = securityEventLogger;

    /// <inheritdoc />
    public async Task<LoginResponse> IssueAsync(ApplicationUser user, Guid? targetCompanyId, CancellationToken cancellationToken)
    {
        var isSuperAdmin = user.IsSuperAdmin || await _userManager.IsInRoleAsync(user, "SuperAdmin");

        var userCompanies = (await _unitOfWork.GetRepository<UserCompany>().GetAllAsync())
            .Where(link => link.UserId == user.Id)
            .OrderByDescending(link => link.IsDefault)
            .ThenBy(link => link.Created)
            .ToList();

        var roleAssignments = (await _unitOfWork.GetRepository<UserRoleCompany>().GetAllAsync())
            .Where(link => link.UserId == user.Id)
            .ToList();

        var effectiveCompanyId = await ResolveEffectiveCompanyIdAsync(
            targetCompanyId,
            userCompanies,
            roleAssignments,
            isSuperAdmin);

        var roleNames = await ResolveRoleNamesAsync(user, effectiveCompanyId, roleAssignments, isSuperAdmin);

        // Refresh-token-id MUST be persisted before the access token is issued so the
        // embedded `refresh_token_id` claim never references a non-existent row.
        var refreshTokenId = Guid.NewGuid();
        var refreshTokenString = _jwtTokenGenerator.GenerateRefreshTokenString();
        var refreshTokenExpiration = _jwtTokenGenerator.GetRefreshTokenExpirationUtc();

        try
        {
            await _unitOfWork.GetRepository<UserRefreshToken>().InsertAsync(new UserRefreshToken(refreshTokenId)
            {
                UserId = user.Id,
                Token = refreshTokenString,
                ExpiryDate = refreshTokenExpiration,
                IsRevoked = false,
                Created = DateTime.UtcNow,
                CreatedBy = user.Email ?? user.UserName
            });

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Refuse to leak a token whose refresh row never persisted.
            throw new UnauthorizedAccessException("The session could not be established.");
        }

        var (token, refreshToken, expiration, _) =
            _jwtTokenGenerator.GenerateToken(user, effectiveCompanyId, roleNames, refreshTokenId, refreshTokenString);

        var successMetadata = System.Text.Json.JsonSerializer.Serialize(new
        {
            companyId = effectiveCompanyId,
            roleCount = roleNames.Count
        });
        await _securityEventLogger.LogAsync(
            SecurityEventType.LoginSucceeded,
            SecurityEventResult.Success,
            user.Id,
            _currentUserService.IpAddress,
            _currentUserService.UserAgent,
            successMetadata,
            cancellationToken);

        return new LoginResponse
        {
            UserId = user.Id,
            UserName = user.UserName ?? user.Email ?? string.Empty,
            Email = user.Email ?? string.Empty,
            CompanyId = effectiveCompanyId,
            Roles = roleNames,
            Token = token,
            RefreshToken = refreshToken,
            Expiration = expiration
        };
    }

    /// <summary>
    /// Resolves the company that should become active for the current authenticated session.
    /// </summary>
    /// <param name="requestedCompanyId">Optional company requested by the client.</param>
    /// <param name="userCompanies">Active user-company links.</param>
    /// <param name="roleAssignments">Active user-role-company assignments.</param>
    /// <param name="isSuperAdmin">Indicates whether the user bypasses tenant restrictions.</param>
    /// <returns>The effective company identifier for the session.</returns>
    private async Task<Guid?> ResolveEffectiveCompanyIdAsync(
        Guid? requestedCompanyId,
        IReadOnlyCollection<UserCompany> userCompanies,
        IReadOnlyCollection<UserRoleCompany> roleAssignments,
        bool isSuperAdmin)
    {
        if (requestedCompanyId.HasValue && requestedCompanyId.Value != Guid.Empty)
        {
            var requestedId = requestedCompanyId.Value;

            if (isSuperAdmin)
            {
                if (await CompanyExistsAsync(requestedId))
                {
                    return requestedId;
                }
            }
            else
            {
                var hasCompanyLink = userCompanies.Any(link => link.CompanyId == requestedId);
                var hasRoleAssignment = roleAssignments.Any(link => link.CompanyId == requestedId);
                if (hasCompanyLink || hasRoleAssignment)
                {
                    return requestedId;
                }
            }
        }

        var defaultCompanyId = userCompanies
            .Select(link => link.CompanyId)
            .FirstOrDefault();

        if (defaultCompanyId != Guid.Empty)
        {
            if (isSuperAdmin || roleAssignments.Any(link => link.CompanyId == defaultCompanyId))
            {
                return defaultCompanyId;
            }
        }

        var firstAssignedCompanyId = roleAssignments
            .Select(link => link.CompanyId)
            .FirstOrDefault();

        if (firstAssignedCompanyId != Guid.Empty)
        {
            return firstAssignedCompanyId;
        }

        if (isSuperAdmin)
        {
            var fallbackCompanyId = (await _unitOfWork.GetRepository<Company>().GetAllAsync())
                .Select(company => company.Id)
                .FirstOrDefault();

            if (fallbackCompanyId != Guid.Empty)
            {
                return fallbackCompanyId;
            }

            return null;
        }

        return null;
    }

    /// <summary>
    /// Resolves all role names that apply to the selected company or fallback access state.
    /// </summary>
    /// <param name="user">The authenticated user.</param>
    /// <param name="companyId">The effective company identifier, if one is available.</param>
    /// <param name="roleAssignments">Active role assignments for the user.</param>
    /// <param name="isSuperAdmin">Indicates whether the user bypasses tenant restrictions.</param>
    /// <returns>The resolved role names for the session.</returns>
    private async Task<IReadOnlyCollection<string>> ResolveRoleNamesAsync(
        ApplicationUser user,
        Guid? companyId,
        IReadOnlyCollection<UserRoleCompany> roleAssignments,
        bool isSuperAdmin)
    {
        if (isSuperAdmin)
        {
            // A user can hold both flags at once (global SuperAdmin + SuperAdminCompany).
            // Without this, the early return below skipped the IsSuperAdminCompany merge
            // that the tenant-scoped branch does further down, so such a user's JWT ended
            // up with only "SuperAdmin" — silently missing "SuperAdminCompany" and 403'ing
            // on every endpoint gated on that role (e.g. GET /Companies), even though the
            // flag was true in the database. Found live while re-testing specs/08 in
            // join_frontb.
            return user.IsSuperAdminCompany
                ? ["SuperAdmin", "SuperAdminCompany"]
                : ["SuperAdmin"];
        }

        if (!companyId.HasValue || companyId.Value == Guid.Empty)
        {
            return ["Basic"];
        }

        var roleIds = roleAssignments
            .Where(link => link.CompanyId == companyId.Value)
            .Select(link => link.RoleId)
            .Distinct()
            .ToArray();

        if (roleIds.Length == 0)
        {
            return ["Basic"];
        }

        var roleRepository = _unitOfWork.GetRepository<ApplicationRole>();
        var roles = await roleRepository.GetAllAsync();

        var resolvedRoles = roles
            .Where(role => roleIds.Contains(role.Id) && !string.IsNullOrWhiteSpace(role.Name))
            .Select(role => role.Name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(role => role)
            .ToArray();

        if (user.IsSuperAdminCompany && !resolvedRoles.Contains("SuperAdminCompany", StringComparer.OrdinalIgnoreCase))
        {
            resolvedRoles = resolvedRoles
                .Append("SuperAdminCompany")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(role => role)
                .ToArray();
        }

        return resolvedRoles.Length > 0
            ? resolvedRoles
            : ["Basic"];
    }

    /// <summary>
    /// Checks whether the requested company exists and is currently active.
    /// </summary>
    /// <param name="companyId">The company identifier to validate.</param>
    /// <returns><c>true</c> when the company exists; otherwise, <c>false</c>.</returns>
    private async Task<bool> CompanyExistsAsync(Guid companyId)
    {
        var company = await _unitOfWork.GetRepository<Company>().GetAsync(companyId);
        return company is not null;
    }
}
