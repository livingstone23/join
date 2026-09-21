using System.Text.Json;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Account.Commands.DisableEmailOtp;

/// <summary>
/// Validates the supplied 6-digit code against the latest active <c>EmailOtpEnableCode</c> row,
/// sets <c>IsEmailOtpEnabled = false</c> on the user, and emits the matching audit event. Locks
/// the code (invalidates it via <c>GcRecord</c>) after 5 failed attempts — same criterion as
/// <c>ConfirmPhoneVerificationCommandHandler</c>. No minimum-active-method check: disabling the
/// user's only active 2FA method is allowed (SPEC 32 acceptance criteria).
/// </summary>
public sealed class DisableEmailOtpCommandHandler(
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUserService,
    IEmailOtpEnableCodeRepository emailOtpCodeRepository,
    ISecurityEventLogger securityEventLogger)
    : IRequestHandler<DisableEmailOtpCommand, Response<bool>>
{
    private const int MaxAttempts = 5;

    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly IEmailOtpEnableCodeRepository _emailOtpCodeRepository = emailOtpCodeRepository;
    private readonly ISecurityEventLogger _securityEventLogger = securityEventLogger;

    public async Task<Response<bool>> Handle(DisableEmailOtpCommand request, CancellationToken cancellationToken)
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

        var code = request.Code?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            return Response<bool>.Error("EMAIL_OTP_CODE_INVALID", ["The verification code must be supplied."]);
        }

        var utcNow = DateTime.UtcNow;
        var stored = await _emailOtpCodeRepository.GetLatestActiveByUserAsync(callerUserId, utcNow, cancellationToken);
        if (stored is null)
        {
            return Response<bool>.Error("EMAIL_OTP_NOT_REQUESTED", ["Request a code via send-code before disabling email OTP."]);
        }

        if (stored.ExpiresAtUtc < utcNow)
        {
            return Response<bool>.Error("EMAIL_OTP_CODE_EXPIRED", ["The verification code has expired. Request a new one."]);
        }

        var attempts = await _emailOtpCodeRepository.IncrementAttemptAsync(stored.Id, utcNow, cancellationToken);
        if (attempts > MaxAttempts)
        {
            await _emailOtpCodeRepository.InvalidateActiveByUserAsync(callerUserId, utcNow, cancellationToken);

            return Response<bool>.Error("EMAIL_OTP_CODE_LOCKED", ["Too many failed attempts. Request a new verification code."]);
        }

        if (!RecoveryCodeHasher.Verify(code, stored.CodeHash))
        {
            return Response<bool>.Error("EMAIL_OTP_CODE_INVALID", ["The verification code is not valid."]);
        }

        var used = await _emailOtpCodeRepository.MarkUsedAsync(stored.Id, utcNow, cancellationToken);
        if (!used)
        {
            // Concurrent disable raced and won — treat as expired so we don't double-flip the flag.
            return Response<bool>.Error("EMAIL_OTP_CODE_EXPIRED", ["The verification code has expired. Request a new one."]);
        }

        user.IsEmailOtpEnabled = false;
        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return Response<bool>.Error(
                "USER_UPDATE_FAILED",
                updateResult.Errors.Select(error => error.Description).ToArray());
        }

        var metadata = JsonSerializer.Serialize(new { userId = callerUserId });
        await _securityEventLogger.LogAsync(
            SecurityEventType.EmailOtpDisabled,
            SecurityEventResult.Success,
            callerUserId,
            _currentUserService.IpAddress,
            _currentUserService.UserAgent,
            metadata,
            cancellationToken);

        return new Response<bool>
        {
            IsSuccess = true,
            Message = "Email OTP disabled.",
            Data = true
        };
    }
}
