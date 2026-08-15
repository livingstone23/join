using System.Text.Json;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Account.Commands.ConfirmPhoneVerification;

/// <summary>
/// Validates the supplied 6-digit code against the latest active PhoneVerificationCode row,
/// sets <c>PhoneNumberConfirmed = true</c> on the user, and emits the matching audit event.
/// Locks the code (invalidates it via <c>GcRecord</c>) after 5 failed attempts
/// (returns <c>PHONE_CODE_LOCKED</c>).
/// </summary>
/// <param name="userManager">ASP.NET Identity manager used to mutate the user.</param>
/// <param name="currentUserService">Resolves the calling user + audit context.</param>
/// <param name="phoneCodeRepository">Persistence for the verification row.</param>
/// <param name="securityEventLogger">Audit logger.</param>
public sealed class ConfirmPhoneVerificationCommandHandler(
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUserService,
    IPhoneVerificationCodeRepository phoneCodeRepository,
    ISecurityEventLogger securityEventLogger)
    : IRequestHandler<ConfirmPhoneVerificationCommand, Response<bool>>
{
    private const int MaxAttempts = 5;

    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly IPhoneVerificationCodeRepository _phoneCodeRepository = phoneCodeRepository;
    private readonly ISecurityEventLogger _securityEventLogger = securityEventLogger;

    public async Task<Response<bool>> Handle(ConfirmPhoneVerificationCommand request, CancellationToken cancellationToken)
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
            return Response<bool>.Error("PHONE_CODE_INVALID", ["The verification code must be supplied."]);
        }

        var utcNow = DateTime.UtcNow;
        var stored = await _phoneCodeRepository.GetLatestActiveByUserAsync(callerUserId, utcNow, cancellationToken);
        if (stored is null)
        {
            return Response<bool>.Error(
                "PHONE_CODE_EXPIRED",
                ["The verification code has expired. Request a new one."]);
        }

        var attempts = await _phoneCodeRepository.IncrementAttemptAsync(stored.Id, utcNow, cancellationToken);
        if (attempts > MaxAttempts)
        {
            // Lock the row by soft-deleting it so subsequent confirmations force a new request.
            await _phoneCodeRepository.InvalidateActiveByUserAsync(callerUserId, utcNow, cancellationToken);

            var lockMetadata = JsonSerializer.Serialize(new { attempts, maxAttempts = MaxAttempts });
            await _securityEventLogger.LogAsync(
                SecurityEventType.PhoneVerificationLocked,
                SecurityEventResult.Success,
                callerUserId,
                _currentUserService.IpAddress,
                _currentUserService.UserAgent,
                lockMetadata,
                cancellationToken);

            return Response<bool>.Error(
                "PHONE_CODE_LOCKED",
                ["Too many failed attempts. Request a new verification code."]);
        }

        if (!RecoveryCodeHasher.Verify(code, stored.CodeHash))
        {
            var failMetadata = JsonSerializer.Serialize(new { attempts });
            await _securityEventLogger.LogAsync(
                SecurityEventType.PhoneVerificationFailed,
                SecurityEventResult.Success,
                callerUserId,
                _currentUserService.IpAddress,
                _currentUserService.UserAgent,
                failMetadata,
                cancellationToken);

            return Response<bool>.Error(
                "PHONE_CODE_INVALID",
                ["The verification code is not valid."]);
        }

        var used = await _phoneCodeRepository.MarkUsedAsync(stored.Id, utcNow, cancellationToken);
        if (!used)
        {
            // Concurrent confirm raced and won — treat as expired so we don't set the wrong phone.
            return Response<bool>.Error(
                "PHONE_CODE_EXPIRED",
                ["The verification code has expired. Request a new one."]);
        }

        user.PhoneNumber = stored.PhoneNumber;
        user.PhoneNumberConfirmed = true;
        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return Response<bool>.Error(
                "USER_UPDATE_FAILED",
                updateResult.Errors.Select(error => error.Description).ToArray());
        }

        var confirmMetadata = JsonSerializer.Serialize(new
        {
            phoneNumber = stored.PhoneNumber
        });
        await _securityEventLogger.LogAsync(
            SecurityEventType.PhoneVerificationConfirmed,
            SecurityEventResult.Success,
            callerUserId,
            _currentUserService.IpAddress,
            _currentUserService.UserAgent,
            confirmMetadata,
            cancellationToken);

        return new Response<bool>
        {
            IsSuccess = true,
            Message = "Phone number confirmed.",
            Data = true
        };
    }
}
