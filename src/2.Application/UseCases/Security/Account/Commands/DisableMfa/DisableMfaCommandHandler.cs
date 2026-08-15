using System.Text.Json;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Account.Commands.DisableMfa;

/// <summary>
/// Disables MFA for the caller. Accepts a fresh TOTP code OR a recovery code, clears
/// the MFA state on <c>AspNetUsers</c>, soft-deletes every recovery code, and emits the
/// matching audit row (<c>MfaDisabled</c> or <c>MfaRecoveryCodeUsed</c>).
/// </summary>
public sealed class DisableMfaCommandHandler(
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUserService,
    IMfaTotpValidator mfaTotpValidator,
    IUserMfaRecoveryCodeRepository recoveryCodeRepository,
    ISecurityEventLogger securityEventLogger)
    : IRequestHandler<DisableMfaCommand, Response<bool>>
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly IMfaTotpValidator _mfaTotpValidator = mfaTotpValidator;
    private readonly IUserMfaRecoveryCodeRepository _recoveryCodeRepository = recoveryCodeRepository;
    private readonly ISecurityEventLogger _securityEventLogger = securityEventLogger;

    public async Task<Response<bool>> Handle(DisableMfaCommand request, CancellationToken cancellationToken)
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

        if (!user.IsMfaEnabled)
        {
            return Response<bool>.Error(
                "MFA_NOT_CONFIGURED",
                ["MFA is not enabled for this account."]);
        }

        var code = request.Code?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            return Response<bool>.Error("MFA_INVALID_CODE", ["A TOTP or recovery code must be supplied."]);
        }

        SecurityEventType resultEvent;
        string eventMetadataReason;

        // TOTP path first.
        if (!string.IsNullOrWhiteSpace(user.MfaSecretKey)
            && code.Length == 6
            && _mfaTotpValidator.ValidateCode(user.MfaSecretKey, code))
        {
            resultEvent = SecurityEventType.MfaDisabled;
            eventMetadataReason = "totp";
        }
        else
        {
            // Recovery code path: walk the unused set and constant-time match.
            var unusedCodes = await _recoveryCodeRepository
                .ListUnusedByUserAsync(callerUserId, cancellationToken);

            var matchedCodeId = (Guid?)null;
            foreach (var stored in unusedCodes)
            {
                if (RecoveryCodeHasher.Verify(code, stored.CodeHash))
                {
                    matchedCodeId = stored.Id;
                    break;
                }
            }

            if (matchedCodeId is null)
            {
                return Response<bool>.Error(
                    "MFA_INVALID_CODE",
                    ["The supplied authenticator or recovery code is not valid."]);
            }

            var utcNow = DateTime.UtcNow;
            await _recoveryCodeRepository.MarkUsedAsync(matchedCodeId.Value, utcNow, cancellationToken);
            resultEvent = SecurityEventType.MfaRecoveryCodeUsed;
            eventMetadataReason = "recovery_code";
        }

        // Clear MFA state on the user row.
        var utcNowForUpdate = DateTime.UtcNow;
        user.IsMfaEnabled = false;
        user.MfaSecretKey = null;
        user.MfaEnabledAtUtc = null;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return Response<bool>.Error(
                "USER_UPDATE_FAILED",
                updateResult.Errors.Select(error => error.Description).ToArray());
        }

        await _recoveryCodeRepository.DeleteAllByUserAsync(callerUserId, utcNowForUpdate, cancellationToken);

        var metadata = JsonSerializer.Serialize(new
        {
            userId = callerUserId,
            reason = eventMetadataReason
        });
        await _securityEventLogger.LogAsync(
            resultEvent,
            SecurityEventResult.Success,
            callerUserId,
            _currentUserService.IpAddress,
            _currentUserService.UserAgent,
            metadata,
            cancellationToken);

        return new Response<bool>
        {
            IsSuccess = true,
            Message = "MFA disabled.",
            Data = true
        };
    }
}
