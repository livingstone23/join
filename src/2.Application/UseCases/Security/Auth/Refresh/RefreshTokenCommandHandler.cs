using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Auth.Refresh;

/// <summary>
/// Handles refresh token rotation requests and returns a renewed authenticated session.
/// </summary>
/// <param name="userManager">ASP.NET Core Identity manager used to resolve the user account.</param>
/// <param name="jwtTokenGenerator">Token generator used to create the renewed token pair.</param>
/// <param name="unitOfWork">Unit of work used to query and persist refresh tokens and company assignments.</param>
public class RefreshTokenCommandHandler(
    UserManager<ApplicationUser> userManager,
    IJwtTokenGenerator jwtTokenGenerator,
    IUnitOfWork unitOfWork)
    : IRequestHandler<RefreshTokenCommand, LoginResponse>
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly IJwtTokenGenerator _jwtTokenGenerator = jwtTokenGenerator;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Validates the incoming refresh token, rotates it, and returns a renewed login response.
    /// </summary>
    /// <param name="request">The refresh token request payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A renewed authenticated session payload.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when the refresh token is invalid, expired, revoked, or no longer usable.</exception>
    public async Task<LoginResponse> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var refreshTokenRepository = _unitOfWork.GetRepository<UserRefreshToken>();
        var refreshTokenEntity = (await refreshTokenRepository.GetAllAsync())
            .FirstOrDefault(token => string.Equals(token.Token, request.RefreshToken, StringComparison.Ordinal));

        if (refreshTokenEntity is null || refreshTokenEntity.IsRevoked || refreshTokenEntity.ExpiryDate <= DateTime.UtcNow)
        {
            throw new UnauthorizedAccessException("The refresh token is invalid, expired, or revoked.");
        }

        var user = await _userManager.FindByIdAsync(refreshTokenEntity.UserId.ToString())
            ?? throw new UnauthorizedAccessException("The refresh token user is no longer available.");

        if (!user.IsActive || user.GcRecord != 0)
        {
            throw new UnauthorizedAccessException("The user account is inactive.");
        }

        refreshTokenEntity.IsRevoked = true;
        refreshTokenEntity.LastModified = DateTime.UtcNow;
        refreshTokenEntity.LastModifiedBy = user.Email ?? user.UserName;
        await refreshTokenRepository.UpdateAsync(refreshTokenEntity);

        var isSuperAdmin = user.IsSuperAdmin || await _userManager.IsInRoleAsync(user, "SuperAdmin");

        var userCompanies = (await _unitOfWork.GetRepository<UserCompany>().GetAllAsync())
            .Where(link => link.UserId == user.Id)
            .OrderByDescending(link => link.IsDefault)
            .ThenBy(link => link.Created)
            .ToList();

        var roleAssignments = (await _unitOfWork.GetRepository<UserRoleCompany>().GetAllAsync())
            .Where(link => link.UserId == user.Id)
            .ToList();

        var effectiveCompanyId = await ResolveEffectiveCompanyIdAsync(userCompanies, roleAssignments, isSuperAdmin);
        var roleNames = await ResolveRoleNamesAsync(effectiveCompanyId, roleAssignments, isSuperAdmin);
        var (accessToken, newRefreshToken, expiration, refreshTokenExpiration) =
            _jwtTokenGenerator.GenerateToken(user, effectiveCompanyId, roleNames);

        await refreshTokenRepository.InsertAsync(new UserRefreshToken
        {
            UserId = user.Id,
            Token = newRefreshToken,
            ExpiryDate = refreshTokenExpiration,
            IsRevoked = false,
            Created = DateTime.UtcNow,
            CreatedBy = user.Email ?? user.UserName
        });

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new LoginResponse
        {
            UserId = user.Id,
            UserName = user.UserName ?? user.Email ?? string.Empty,
            Email = user.Email ?? string.Empty,
            CompanyId = effectiveCompanyId,
            Roles = roleNames,
            Token = accessToken,
            RefreshToken = newRefreshToken,
            Expiration = expiration
        };
    }

    /// <summary>
    /// Resolves the company that should become active for the renewed session.
    /// </summary>
    /// <param name="userCompanies">Active user-company links.</param>
    /// <param name="roleAssignments">Active user-role-company assignments.</param>
    /// <param name="isSuperAdmin">Indicates whether the user bypasses tenant restrictions.</param>
    /// <returns>The effective company identifier for the renewed session.</returns>
    private async Task<Guid?> ResolveEffectiveCompanyIdAsync(
        IReadOnlyCollection<UserCompany> userCompanies,
        IReadOnlyCollection<UserRoleCompany> roleAssignments,
        bool isSuperAdmin)
    {
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
        }

        return null;
    }

    /// <summary>
    /// Resolves all effective role names for the renewed session.
    /// </summary>
    /// <param name="companyId">The active company identifier, if one is available.</param>
    /// <param name="roleAssignments">Active role assignments for the user.</param>
    /// <param name="isSuperAdmin">Indicates whether the user bypasses tenant restrictions.</param>
    /// <returns>The ordered role names for the renewed session.</returns>
    private async Task<IReadOnlyCollection<string>> ResolveRoleNamesAsync(
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

        var roles = await _unitOfWork.GetRepository<ApplicationRole>().GetAllAsync();
        var resolvedRoles = roles
            .Where(role => roleIds.Contains(role.Id) && !string.IsNullOrWhiteSpace(role.Name))
            .Select(role => role.Name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(role => role)
            .ToArray();

        return resolvedRoles.Length > 0 ? resolvedRoles : ["Basic"];
    }
}
