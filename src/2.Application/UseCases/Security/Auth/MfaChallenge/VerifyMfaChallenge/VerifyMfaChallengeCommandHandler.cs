using System.Text.Json;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Auth.MfaChallenge.VerifyMfaChallenge;

/// <summary>
/// Validates a pending <see cref="MfaLoginChallenge"/>'s code — live TOTP via
/// <see cref="IMfaTotpValidator"/> against <c>user.MfaSecretKey</c>, or the emailed code against
/// <see cref="MfaLoginChallenge.EmailCodeHash"/> — and, only on success, delegates to
/// <see cref="IAuthenticatedSessionIssuer"/> to resolve company/roles and issue the real JWT.
/// 5 failed attempts on the same <c>challengeToken</c> invalidate the whole challenge (both
/// methods), same criterion as <c>ConfirmPhoneVerificationCommandHandler</c> (SPEC 30).
/// </summary>
/// <param name="userManager">ASP.NET Identity manager used to resolve the challenge's owner.</param>
/// <param name="challengeRepository">Persistence for the login-challenge row.</param>
/// <param name="mfaTotpValidator">Validates live TOTP codes against the user's enrolled secret.</param>
/// <param name="authenticatedSessionIssuer">Resolves company/roles and issues the JWT on success.</param>
/// <param name="currentUserService">Resolves the caller's IP/user-agent for audit logging.</param>
/// <param name="securityEventLogger">Audit logger.</param>
public sealed class VerifyMfaChallengeCommandHandler(
    UserManager<ApplicationUser> userManager,
    IMfaLoginChallengeRepository challengeRepository,
    IMfaTotpValidator mfaTotpValidator,
    IAuthenticatedSessionIssuer authenticatedSessionIssuer,
    ICurrentUserService currentUserService,
    ISecurityEventLogger securityEventLogger)
    : IRequestHandler<VerifyMfaChallengeCommand, Response<LoginResponse>>
{
    private const int MaxAttempts = 5;

    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly IMfaLoginChallengeRepository _challengeRepository = challengeRepository;
    private readonly IMfaTotpValidator _mfaTotpValidator = mfaTotpValidator;
    private readonly IAuthenticatedSessionIssuer _authenticatedSessionIssuer = authenticatedSessionIssuer;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly ISecurityEventLogger _securityEventLogger = securityEventLogger;

    public async Task<Response<LoginResponse>> Handle(VerifyMfaChallengeCommand request, CancellationToken cancellationToken)
    {
        var method = request.Method.Trim().ToLowerInvariant();
        if (method != MfaMethodResolver.Totp && method != MfaMethodResolver.Email)
        {
            return Response<LoginResponse>.Error("CHALLENGE_METHOD_NOT_AVAILABLE", ["Unsupported MFA method."]);
        }

        var utcNow = DateTime.UtcNow;
        var tokenHash = OpaqueTokenHasher.Hash(request.ChallengeToken.Trim());
        var challenge = await _challengeRepository.GetActiveByTokenHashAsync(tokenHash, utcNow, cancellationToken);
        if (challenge is null)
        {
            return Response<LoginResponse>.Error("CHALLENGE_NOT_FOUND", ["The MFA challenge was not found."]);
        }

        if (challenge.ExpiresAtUtc < utcNow)
        {
            return Response<LoginResponse>.Error("CHALLENGE_EXPIRED", ["The MFA challenge has expired. Log in again."]);
        }

        var user = await _userManager.FindByIdAsync(challenge.UserId.ToString());
        if (user is null || !user.IsActive || user.GcRecord != 0)
        {
            return Response<LoginResponse>.Error("CHALLENGE_NOT_FOUND", ["The MFA challenge was not found."]);
        }

        var availableMethods = MfaMethodResolver.ResolveAvailable(user);
        if (!availableMethods.Contains(method))
        {
            return Response<LoginResponse>.Error("CHALLENGE_METHOD_NOT_AVAILABLE", ["The requested method is not active for this user."]);
        }

        // Shared counter across both methods: 5 failures on either lock the WHOLE challenge,
        // not just the method used — same fail-closed posture as ConfirmPhoneVerificationCommandHandler.
        var attempts = await _challengeRepository.IncrementAttemptAsync(challenge.Id, utcNow, cancellationToken);
        if (attempts > MaxAttempts)
        {
            await _challengeRepository.InvalidateAsync(challenge.Id, utcNow, cancellationToken);

            var lockMetadata = JsonSerializer.Serialize(new { attempts, maxAttempts = MaxAttempts, method });
            await _securityEventLogger.LogAsync(
                SecurityEventType.LoginMfaChallengeLocked,
                SecurityEventResult.Success,
                user.Id,
                _currentUserService.IpAddress,
                _currentUserService.UserAgent,
                lockMetadata,
                cancellationToken);

            return Response<LoginResponse>.Error(
                "CHALLENGE_LOCKED",
                ["Too many failed attempts. Log in again."]);
        }

        var codeIsValid = method == MfaMethodResolver.Totp
            ? ValidateTotpCode(user, request.Code)
            : ValidateEmailCode(challenge, request.Code, utcNow);

        if (!codeIsValid)
        {
            var failMetadata = JsonSerializer.Serialize(new { attempts, method });
            await _securityEventLogger.LogAsync(
                SecurityEventType.LoginMfaChallengeFailed,
                SecurityEventResult.Success,
                user.Id,
                _currentUserService.IpAddress,
                _currentUserService.UserAgent,
                failMetadata,
                cancellationToken);

            return Response<LoginResponse>.Error("CHALLENGE_INVALID_CODE", ["The supplied code is not valid."]);
        }

        var consumed = await _challengeRepository.MarkConsumedAsync(challenge.Id, utcNow, cancellationToken);
        if (!consumed)
        {
            // Raced with another verify/lock on the same challenge — treat as no longer available.
            return Response<LoginResponse>.Error("CHALLENGE_EXPIRED", ["The MFA challenge is no longer available."]);
        }

        var sessionResponse = await _authenticatedSessionIssuer.IssueAsync(user, challenge.TargetCompanyId, cancellationToken);

        var verifiedMetadata = JsonSerializer.Serialize(new { method });
        await _securityEventLogger.LogAsync(
            SecurityEventType.LoginMfaChallengeVerified,
            SecurityEventResult.Success,
            user.Id,
            _currentUserService.IpAddress,
            _currentUserService.UserAgent,
            verifiedMetadata,
            cancellationToken);

        return new Response<LoginResponse>
        {
            IsSuccess = true,
            Message = "MFA challenge verified.",
            Data = sessionResponse
        };
    }

    /// <summary>
    /// Validates a live TOTP code against the user's enrolled secret. Fails closed when the
    /// user has no secret configured (should be unreachable — <c>totp</c> would not be in
    /// <see cref="MfaMethodResolver.ResolveAvailable"/> without one).
    /// </summary>
    private bool ValidateTotpCode(ApplicationUser user, string code)
    {
        if (string.IsNullOrWhiteSpace(user.MfaSecretKey))
        {
            return false;
        }

        return _mfaTotpValidator.ValidateCode(user.MfaSecretKey, code);
    }

    /// <summary>
    /// Validates the supplied code against the challenge's own emailed-code hash. Fails closed
    /// when no email code was ever sent for this challenge, or it has expired.
    /// </summary>
    private static bool ValidateEmailCode(MfaLoginChallenge challenge, string code, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(challenge.EmailCodeHash))
        {
            return false;
        }

        if (challenge.EmailCodeExpiresAtUtc is null || challenge.EmailCodeExpiresAtUtc < utcNow)
        {
            return false;
        }

        return RecoveryCodeHasher.Verify(code, challenge.EmailCodeHash);
    }
}
