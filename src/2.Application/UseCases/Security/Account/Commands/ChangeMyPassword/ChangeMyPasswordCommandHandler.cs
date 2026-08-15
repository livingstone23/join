using System.Text.Json;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Account.Commands.ChangeMyPassword;

/// <summary>
/// Handles the authenticated password-change flow. Records a <c>PasswordChanged</c>
/// audit event on success (SPEC 26 / F5).
/// </summary>
/// <param name="userManager">ASP.NET Core Identity manager used to mutate the user store.</param>
/// <param name="currentUserService">HTTP context for IP / User-Agent on audit logs.</param>
/// <param name="securityEventLogger">Audit logger.</param>
public sealed class ChangeMyPasswordCommandHandler(
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUserService,
    ISecurityEventLogger securityEventLogger)
    : IRequestHandler<ChangeMyPasswordCommand, Response<bool>>
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly ISecurityEventLogger _securityEventLogger = securityEventLogger;

    public async Task<Response<bool>> Handle(ChangeMyPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null || !user.IsActive || user.GcRecord != 0)
        {
            return Response<bool>.Error("ACCOUNT_NOT_FOUND", ["Authenticated account was not found."]);
        }

        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return Response<bool>.Error("PASSWORD_CONFIRMATION_MISMATCH", ["New password and confirmation do not match."]);
        }

        var result = await _userManager.ChangePasswordAsync(user, request.OldPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return Response<bool>.Error(
                "PASSWORD_CHANGE_FAILED",
                result.Errors.Select(error => error.Description).ToArray());
        }

        var metadata = JsonSerializer.Serialize(new { userId = user.Id });
        await _securityEventLogger.LogAsync(
            SecurityEventType.PasswordChanged,
            SecurityEventResult.Success,
            user.Id,
            _currentUserService.IpAddress,
            _currentUserService.UserAgent,
            metadata,
            cancellationToken);

        return new Response<bool>
        {
            IsSuccess = true,
            Message = "Password changed successfully.",
            Data = true
        };
    }
}
