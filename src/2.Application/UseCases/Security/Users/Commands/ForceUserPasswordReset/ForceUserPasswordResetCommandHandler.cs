using JOIN.Application.Common;
using JOIN.Application.Common.Email;
using JOIN.Application.Common.Options;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Security.Users.Commands.ForceUserPasswordReset;

/// <summary>
/// SPEC 27 item 17 — admin forces a password reset on a target user. The handler:
/// 1. Guards against inactive users (because <c>UserManager.FindByIdAsync</c> would
///    otherwise return null under the EF global query filter).
/// 2. Mints an Identity password-reset token and emails the reset link.
/// 3. If the email send succeeds, revokes all active refresh tokens.
/// If the email send fails, tokens are NOT revoked — losing a session without
/// explanation is worse than the opposite.
/// </summary>
public sealed class ForceUserPasswordResetCommandHandler(
    UserManager<ApplicationUser> userManager,
    IUserAdminRepository userAdminRepository,
    IEmailService emailService,
    ICurrentUserService currentUserService,
    IOptions<AppUrlsOptions> appUrls)
    : IRequestHandler<ForceUserPasswordResetCommand, Response<bool>>
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly IUserAdminRepository _userAdminRepository = userAdminRepository;
    private readonly IEmailService _emailService = emailService;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly AppUrlsOptions _appUrls = appUrls.Value;

    public async Task<Response<bool>> Handle(ForceUserPasswordResetCommand request, CancellationToken cancellationToken)
    {
        var companyId = _currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<bool>.Error("TENANT_REQUIRED", ["A tenant context is required to force a password reset."]);
        }

        var snapshot = await _userAdminRepository.GetAdminSnapshotAsync(request.UserId, companyId, cancellationToken);
        if (snapshot is null || !snapshot.HasMembership)
        {
            return Response<bool>.Error("USER_NOT_FOUND", ["The user was not found in this tenant."]);
        }

        if (!snapshot.IsActive)
        {
            return Response<bool>.Error("USER_INACTIVE", ["Cannot force a password reset on an inactive account."]);
        }

        if (string.IsNullOrWhiteSpace(_appUrls.FrontendBaseUrl))
        {
            return Response<bool>.Error("FRONTEND_URL_NOT_CONFIGURED", ["Frontend base URL is not configured."]);
        }

        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return Response<bool>.Error("USER_NOT_FOUND", ["The user was not found or has no email on file."]);
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var link = AuthEmailTemplates.BuildLink(_appUrls.FrontendBaseUrl, AuthEmailTemplates.ResetPasswordPathName, user.Email, token);
        var htmlBody = AuthEmailTemplates.BuildForcedReset(user.FirstName ?? user.Email, link, request.Reason ?? "Administrative reset.");

        var sent = await _emailService.SendEmailAsync(user.Email, "Your JOIN CRM password has been reset", htmlBody);
        if (!sent)
        {
            return Response<bool>.Error("EMAIL_DELIVERY_FAILED", ["Unable to send the password reset email."]);
        }

        await _userAdminRepository.RevokeActiveRefreshTokensAsync(user.Id, DateTime.UtcNow, cancellationToken);

        return new Response<bool>
        {
            IsSuccess = true,
            Message = "Password reset email sent.",
            Data = true
        };
    }
}
