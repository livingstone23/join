using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Workspaces;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using JOIN.Domain.Exceptions;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Workspaces.Commands.SwitchCompany;

/// <summary>
/// Switches the caller's active company context and issues a renewed JWT
/// scoped to it. Backs <c>POST /api/v1/workspaces/switch-company</c>
/// (specs/06-mi-cuenta-empresas.md — "la acción más sensible del módulo").
/// Access check and role/token issuance mirror <c>LoginCommandHandler</c>'s
/// logic exactly, scoped to the one requested company instead of resolving
/// an "effective" one. Throws <see cref="DomainException"/> (→ 400 via
/// <c>GlobalExceptionHandler</c>) when the caller has no access to the
/// requested company — the token is never issued in that case.
/// </summary>
/// <param name="userManager">ASP.NET Core Identity manager used to load the authenticated user.</param>
/// <param name="jwtTokenGenerator">Token generator used to create the renewed JWT.</param>
/// <param name="unitOfWork">Unit of work used to query company/role assignments and persist the new refresh token.</param>
public sealed class SwitchCompanyCommandHandler(
    UserManager<ApplicationUser> userManager,
    IJwtTokenGenerator jwtTokenGenerator,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SwitchCompanyCommand, Response<SwitchCompanyResponseDto>>
{
    public async Task<Response<SwitchCompanyResponseDto>> Handle(
        SwitchCompanyCommand request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString())
            ?? throw new DomainException("USER_NOT_FOUND", "The authenticated user is not available.");

        var isSuperAdmin = user.IsSuperAdmin || await userManager.IsInRoleAsync(user, "SuperAdmin");

        var userCompanies = (await unitOfWork.GetRepository<UserCompany>().GetAllAsync())
            .Where(link => link.UserId == user.Id)
            .ToList();

        var roleAssignments = (await unitOfWork.GetRepository<UserRoleCompany>().GetAllAsync())
            .Where(link => link.UserId == user.Id)
            .ToList();

        // Access check — mirrors LoginCommandHandler.ResolveEffectiveCompanyIdAsync,
        // scoped to the one company the caller asked to switch into.
        var hasAccess = isSuperAdmin
            || userCompanies.Any(link => link.CompanyId == request.CompanyId)
            || roleAssignments.Any(link => link.CompanyId == request.CompanyId);

        if (!hasAccess)
        {
            throw new DomainException("COMPANY_ACCESS_DENIED", "You do not have access to the requested company.");
        }

        if (isSuperAdmin)
        {
            var company = await unitOfWork.GetRepository<Company>().GetAsync(request.CompanyId);
            if (company is null)
            {
                throw new DomainException("COMPANY_NOT_FOUND", "The requested company does not exist.");
            }
        }

        var roleNames = await ResolveRoleNamesAsync(user, request.CompanyId, roleAssignments, isSuperAdmin);

        // Refresh-token-id MUST be persisted before the access token is issued so the
        // embedded refresh_token_id claim never references a non-existent row
        // (same ordering LoginCommandHandler uses).
        var refreshTokenId = Guid.NewGuid();
        var refreshTokenString = jwtTokenGenerator.GenerateRefreshTokenString();
        var refreshTokenExpiration = jwtTokenGenerator.GetRefreshTokenExpirationUtc();

        try
        {
            await unitOfWork.GetRepository<UserRefreshToken>().InsertAsync(new UserRefreshToken(refreshTokenId)
            {
                UserId = user.Id,
                Token = refreshTokenString,
                ExpiryDate = refreshTokenExpiration,
                IsRevoked = false,
                Created = DateTime.UtcNow,
                CreatedBy = user.Email ?? user.UserName
            });

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            throw new DomainException("SESSION_NOT_ESTABLISHED", "The session could not be established.");
        }

        var (token, refreshToken, expiration, _) =
            jwtTokenGenerator.GenerateToken(user, request.CompanyId, roleNames, refreshTokenId, refreshTokenString);

        return new Response<SwitchCompanyResponseDto>
        {
            IsSuccess = true,
            Message = "OK",
            Data = new SwitchCompanyResponseDto
            {
                CompanyId = request.CompanyId,
                AccessToken = token,
                RefreshToken = refreshToken,
                AccessTokenExpirationUtc = expiration
            }
        };
    }

    /// <summary>Same resolution rules as <c>LoginCommandHandler.ResolveRoleNamesAsync</c>.</summary>
    private async Task<IReadOnlyCollection<string>> ResolveRoleNamesAsync(
        ApplicationUser user,
        Guid companyId,
        IReadOnlyCollection<UserRoleCompany> roleAssignments,
        bool isSuperAdmin)
    {
        if (isSuperAdmin)
        {
            return ["SuperAdmin"];
        }

        var roleIds = roleAssignments
            .Where(link => link.CompanyId == companyId)
            .Select(link => link.RoleId)
            .Distinct()
            .ToArray();

        if (roleIds.Length == 0)
        {
            return ["Basic"];
        }

        var roles = await unitOfWork.GetRepository<ApplicationRole>().GetAllAsync();

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

        return resolvedRoles.Length > 0 ? resolvedRoles : ["Basic"];
    }
}
