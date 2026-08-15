using System.Text.Json;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Account.Commands.EnableMfa;

/// <summary>
/// Confirms a previously issued MFA enrolment by validating the supplied TOTP code.
/// Activates the user row (<c>IsMfaEnabled = true</c>, <c>MfaEnabledAtUtc = UtcNow</c>)
/// and emits a <c>MfaEnabled</c> audit row.
/// </summary>
/// <param name="userManager">Identity manager used to mutate <c>AspNetUsers</c>.</param>
/// <param name="currentUserService">Resolves the calling user id + audit context.</param>
/// <param name="mfaTotpValidator">TOTP validator against the enrolled secret.</param>
/// <param name="securityEventLogger">Audit logger.</param>
public sealed class EnableMfaCommandHandler(
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUserService,
    IMfaTotpValidator mfaTotpValidator,
    ISecurityEventLogger securityEventLogger)
    : IRequestHandler<EnableMfaCommand, Response<bool>>
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly IMfaTotpValidator _mfaTotpValidator = mfaTotpValidator;
    private readonly ISecurityEventLogger _securityEventLogger = securityEventLogger;

    public async Task<Response<bool>> Handle(EnableMfaCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var callerUserId) || callerUserId == Guid.Empty)
        {
            return Response<bool>.Error("USER_NOT_FOUND", ["The authenticated user is not available."]);
        }

        var user = await _userManager.FindByIdAsync(callerUserId.ToString());
        if (user is null || !user.IsActive || user.GcRecord != 0)
        {
            return Response<bool>.Error("ACCOUNT_NOT_FOUND", ["Authenticated account was not found."]);
        }

        if (string.IsNullOrWhiteSpace(user.MfaSecretKey))
        {
            return Response<bool>.Error(
                "MFA_NOT_CONFIGURED",
                ["MFA setup has not been run for this user."]);
        }

        if (!_mfaTotpValidator.ValidateCode(user.MfaSecretKey, request.Code))
        {
            return Response<bool>.Error(
                "MFA_INVALID_CODE",
                ["The supplied authenticator code is not valid."]);
        }

        var utcNow = DateTime.UtcNow;
        user.IsMfaEnabled = true;
        user.MfaEnabledAtUtc = utcNow;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return Response<bool>.Error(
                "USER_UPDATE_FAILED",
                updateResult.Errors.Select(error => error.Description).ToArray());
        }

        var metadata = JsonSerializer.Serialize(new { userId = callerUserId });
        await _securityEventLogger.LogAsync(
            SecurityEventType.MfaEnabled,
            SecurityEventResult.Success,
            callerUserId,
            _currentUserService.IpAddress,
            _currentUserService.UserAgent,
            metadata,
            cancellationToken);

        return new Response<bool>
        {
            IsSuccess = true,
            Message = "MFA enabled.",
            Data = true
        };
    }
}
