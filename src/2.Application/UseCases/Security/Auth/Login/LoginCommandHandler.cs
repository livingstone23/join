using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;



namespace JOIN.Application.UseCases.Security.Auth.Login;



/// <summary>
/// Handles authentication requests and resolves the effective company and role for the session.
/// </summary>
/// <param name="userManager">ASP.NET Core Identity manager used to validate the user credentials.</param>
/// <param name="jwtTokenGenerator">Token generator used to create the JWT returned to the client.</param>
/// <param name="unitOfWork">Unit of work used to query tenant memberships and role assignments.</param>
public class LoginCommandHandler(
    UserManager<ApplicationUser> userManager,
    IJwtTokenGenerator jwtTokenGenerator,
    IUnitOfWork unitOfWork)
    : IRequestHandler<LoginCommand, LoginResponse>
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly IJwtTokenGenerator _jwtTokenGenerator = jwtTokenGenerator;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Authenticates the user and creates the login response payload.
    /// </summary>
    /// <param name="request">The login request payload.</param>
    /// <param name="cancellationToken">The cancellation token for the current operation.</param>
    /// <returns>The authenticated session payload.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when the credentials or tenant context are invalid.</exception>
    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim();

        var user = await _userManager.FindByEmailAsync(normalizedEmail)
            ?? throw new UnauthorizedAccessException("Invalid email or password.");

        if (!user.IsActive || user.GcRecord != 0)
        {
            throw new UnauthorizedAccessException("The user account is inactive.");
        }

        var passwordIsValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordIsValid)
        {
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

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
            request.TargetCompanyId,
            userCompanies,
            roleAssignments,
            isSuperAdmin);

        var roleNames = await ResolveRoleNamesAsync(user, effectiveCompanyId, roleAssignments, isSuperAdmin);
        var (token, refreshToken, expiration, refreshTokenExpiration) =
            _jwtTokenGenerator.GenerateToken(user, effectiveCompanyId, roleNames);

        await _unitOfWork.GetRepository<UserRefreshToken>().InsertAsync(new UserRefreshToken
        {
            UserId = user.Id,
            Token = refreshToken,
            ExpiryDate = refreshTokenExpiration,
            IsRevoked = false,
            Created = DateTime.UtcNow,
            CreatedBy = user.Email ?? user.UserName
        });

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new LoginResponse
        {
            UserId = user.Id,
            UserName = user.UserName ?? normalizedEmail,
            Email = user.Email ?? normalizedEmail,
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
    /// <exception cref="UnauthorizedAccessException">Thrown when no valid company can be resolved.</exception>
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
            return ["SuperAdmin"];
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
