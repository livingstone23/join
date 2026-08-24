using System.Text.Json;
using JOIN.Application.Common;
using JOIN.Application.Common.Email;
using JOIN.Application.Common.Options;
using JOIN.Application.DTO.Security.User;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Security.Users.Commands.InviteUser;

/// <summary>
/// Implements the SPEC 27 item-15 decision tree. Creates the user, the membership, and
/// the per-role assignments in the caller's tenant, then mints an Identity password-reset
/// token and emails the setup link. If the email send fails, the entire EF transaction
/// rolls back (F0 in the spec).
/// </summary>
public sealed class InviteUserCommandHandler(
    UserManager<ApplicationUser> userManager,
    IUnitOfWork unitOfWork,
    IUserAdminRepository userAdminRepository,
    ICurrentUserService currentUserService,
    IEmailService emailService,
    IOptions<AppUrlsOptions> appUrls,
    ISecurityEventLogger securityEventLogger)
    : IRequestHandler<InviteUserCommand, Response<InviteUserResultDto>>
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly IUserAdminRepository _userAdminRepository = userAdminRepository;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly IEmailService _emailService = emailService;
    private readonly AppUrlsOptions _appUrls = appUrls.Value;
    private readonly ISecurityEventLogger _securityEventLogger = securityEventLogger;

    public async Task<Response<InviteUserResultDto>> Handle(InviteUserCommand request, CancellationToken cancellationToken)
    {
        var companyId = _currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<InviteUserResultDto>.Error("TENANT_REQUIRED", ["A tenant context is required to invite a user."]);
        }

        if (string.IsNullOrWhiteSpace(_appUrls.FrontendBaseUrl))
        {
            return Response<InviteUserResultDto>.Error("FRONTEND_URL_NOT_CONFIGURED", ["Frontend base URL is not configured."]);
        }

        var email = request.Email.Trim().ToLowerInvariant();

        // Validate every role belongs to the calling tenant before writing anything.
        var existingRoleIds = await _userAdminRepository.FilterExistingRoleIdsAsync(request.RoleIds, companyId, cancellationToken);
        if (existingRoleIds.Count != request.RoleIds.Count)
        {
            return Response<InviteUserResultDto>.Error("ROLE_NOT_FOUND", ["One or more role ids are invalid for this tenant."]);
        }

        var existingUser = await _userManager.FindByEmailAsync(email);

        InviteOutcome outcome;
        ApplicationUser user;

        if (existingUser is null)
        {
            // Branch 1 — create fresh user.
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                IsActive = true,
                EmailConfirmed = false,
                Created = DateTime.UtcNow,
                CreatedBy = _currentUserService.UserId
            };
            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                var errs = createResult.Errors.Select(e => e.Description).ToArray();
                return Response<InviteUserResultDto>.Error("USER_CREATE_FAILED", errs);
            }
            outcome = InviteOutcome.Created;
        }
        else
        {
            user = existingUser;
            var hasMembership = await _userAdminRepository.HasCompanyMembershipAsync(user.Id, companyId, cancellationToken);
            if (!hasMembership)
            {
                // Branch 2 — multi-tenant add.
                outcome = InviteOutcome.MembershipAdded;
            }
            else if (!await _userManager.HasPasswordAsync(user))
            {
                // Branch 3 — re-send pending invitation, replace role assignments for this tenant.
                await ReplaceUserRoleCompaniesAsync(user.Id, companyId, request.RoleIds, cancellationToken);
                outcome = InviteOutcome.InvitationResent;
            }
            else
            {
                // Branch 4 — user is fully active in this tenant.
                return Response<InviteUserResultDto>.Error("USER_ALREADY_EXISTS",
                    ["This email already has an active account in the tenant."]);
            }
        }

        // Membership row — only create when not already present (branches 1 and 2).
        if (outcome == InviteOutcome.Created || outcome == InviteOutcome.MembershipAdded)
        {
            var hasMembership = await _userAdminRepository.HasCompanyMembershipAsync(user.Id, companyId, cancellationToken);
            if (!hasMembership)
            {
                var isDefault = !await _userAdminRepository.HasAnyCompanyAsync(user.Id, cancellationToken);
                var utcNow = DateTime.UtcNow;
                var userCompany = new UserCompany
                {
                    UserId = user.Id,
                    CompanyId = companyId,
                    IsDefault = isDefault,
                    Created = utcNow,
                    CreatedBy = _currentUserService.UserId
                };
                await _unitOfWork.GetRepository<UserCompany>().InsertAsync(userCompany);
            }

            await ReplaceUserRoleCompaniesAsync(user.Id, companyId, request.RoleIds, cancellationToken);
        }

        // Flush the queued UserCompany / UserRoleCompany inserts inside the open
        // transaction. Without this call TransactionBehavior.CommitAsync would commit
        // only the row produced by UserManager.CreateAsync — the membership / role
        // assignments only live in the change tracker and would silently disappear,
        // leaving the new user without any tenant scope and breaking every downstream
        // query that joins UserCompanies (e.g. ChangeUserStatus → USER_NOT_FOUND).
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Mint password-reset token (works for both no-password and existing-password cases).
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var link = AuthEmailTemplates.BuildLink(_appUrls.FrontendBaseUrl, AuthEmailTemplates.SetupPasswordPathName, email, token);

        var companyName = await _userAdminRepository.GetCompanyNameAsync(companyId, cancellationToken) ?? "your company";
        var htmlBody = AuthEmailTemplates.BuildInvitation(request.FirstName.Trim(), companyName, link);

        var sent = await _emailService.SendEmailAsync(email, "You have been invited to JOIN CRM", htmlBody);
        if (!sent)
        {
            // F0 — TransactionBehavior rolls back the entire unit of work.
            return Response<InviteUserResultDto>.Error("EMAIL_DELIVERY_FAILED", ["Unable to send the invitation email."]);
        }

        // Audit row (best-effort).
        var metadata = JsonSerializer.Serialize(new { userId = user.Id, outcome = outcome.ToString(), roleCount = request.RoleIds.Count });
        await _securityEventLogger.LogAsync(
            SecurityEventType.MfaSetupInitiated, // closest existing event — proper SPEC 29 audit row arrives later
            SecurityEventResult.Success,
            user.Id,
            _currentUserService.IpAddress,
            _currentUserService.UserAgent,
            metadata,
            cancellationToken);

        return new Response<InviteUserResultDto>
        {
            IsSuccess = true,
            Message = "Invitation sent.",
            Data = new InviteUserResultDto(user.Id, email, outcome)
        };
    }

    /// <summary>
    /// Replaces the user's <c>UserRoleCompany</c> assignments in the supplied tenant with
    /// exactly the supplied role ids. Used by the create branch and the re-send branch.
    /// </summary>
    private async Task ReplaceUserRoleCompaniesAsync(Guid userId, Guid companyId, IReadOnlyList<Guid> roleIds, CancellationToken ct)
    {
        var repo = _unitOfWork.GetRepository<UserRoleCompany>();
        var utcNow = DateTime.UtcNow;
        foreach (var roleId in roleIds)
        {
            var assignment = new UserRoleCompany
            {
                UserId = userId,
                RoleId = roleId,
                CompanyId = companyId,
                Created = utcNow,
                CreatedBy = _currentUserService.UserId
            };
            await repo.InsertAsync(assignment);
        }
    }
}
