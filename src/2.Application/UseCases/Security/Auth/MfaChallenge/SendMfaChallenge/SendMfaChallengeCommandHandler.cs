using System.Security.Cryptography;
using System.Text.Json;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Auth.MfaChallenge.SendMfaChallenge;

/// <summary>
/// (Re)sends the 6-digit email code for a pending <see cref="MfaLoginChallenge"/>. Anonymous —
/// the caller only has the opaque <c>ChallengeToken</c>, never a JWT, at this point in the flow.
/// Enforces a 60s resend cooldown via <see cref="MfaLoginChallenge.EmailSentAtUtc"/> (not
/// <c>[EnableRateLimiting]</c>, which is per-IP/generic and can't tell the client how many
/// seconds remain).
/// </summary>
/// <param name="userManager">ASP.NET Identity manager used to resolve the challenge's owner.</param>
/// <param name="challengeRepository">Persistence for the login-challenge row.</param>
/// <param name="emailService">Email adapter (SendGrid).</param>
/// <param name="currentUserService">Resolves the caller's IP/user-agent for audit logging.</param>
/// <param name="securityEventLogger">Audit logger.</param>
public sealed class SendMfaChallengeCommandHandler(
    UserManager<ApplicationUser> userManager,
    IMfaLoginChallengeRepository challengeRepository,
    IEmailService emailService,
    ICurrentUserService currentUserService,
    ISecurityEventLogger securityEventLogger)
    : IRequestHandler<SendMfaChallengeCommand, Response<bool>>
{
    private const int ResendCooldownSeconds = 60;
    private const int CodeNumericLength = 6;

    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly IMfaLoginChallengeRepository _challengeRepository = challengeRepository;
    private readonly IEmailService _emailService = emailService;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly ISecurityEventLogger _securityEventLogger = securityEventLogger;

    public async Task<Response<bool>> Handle(SendMfaChallengeCommand request, CancellationToken cancellationToken)
    {
        var method = request.Method.Trim().ToLowerInvariant();
        if (method != MfaMethodResolver.Email)
        {
            return Response<bool>.Error(
                "CHALLENGE_METHOD_NOT_RESENDABLE",
                ["Only the email method can be resent; totp codes are generated live by the authenticator app."]);
        }

        var utcNow = DateTime.UtcNow;
        var tokenHash = OpaqueTokenHasher.Hash(request.ChallengeToken.Trim());
        var challenge = await _challengeRepository.GetActiveByTokenHashAsync(tokenHash, utcNow, cancellationToken);
        if (challenge is null)
        {
            return Response<bool>.Error("CHALLENGE_NOT_FOUND", ["The MFA challenge was not found."]);
        }

        if (challenge.ExpiresAtUtc < utcNow)
        {
            return Response<bool>.Error("CHALLENGE_EXPIRED", ["The MFA challenge has expired. Log in again."]);
        }

        var user = await _userManager.FindByIdAsync(challenge.UserId.ToString());
        if (user is null || !user.IsActive || user.GcRecord != 0)
        {
            return Response<bool>.Error("CHALLENGE_NOT_FOUND", ["The MFA challenge was not found."]);
        }

        if (!user.IsEmailOtpEnabled)
        {
            return Response<bool>.Error("CHALLENGE_METHOD_NOT_AVAILABLE", ["Email OTP is not active for this user."]);
        }

        if (challenge.EmailSentAtUtc is not null)
        {
            var secondsSinceLastSend = (utcNow - challenge.EmailSentAtUtc.Value).TotalSeconds;
            if (secondsSinceLastSend < ResendCooldownSeconds)
            {
                var remainingSeconds = (int)Math.Ceiling(ResendCooldownSeconds - secondsSinceLastSend);
                return Response<bool>.Error(
                    "CHALLENGE_SEND_COOLDOWN",
                    [$"Please wait {remainingSeconds} more second(s) before requesting a new code."]);
            }
        }

        var code = GenerateNumericCode(CodeNumericLength);
        // The email code never outlives the challenge itself — send never extends ExpiresAtUtc.
        await _challengeRepository.UpdateEmailCodeAsync(
            challenge.Id,
            RecoveryCodeHasher.Hash(code),
            challenge.ExpiresAtUtc,
            utcNow,
            cancellationToken);

        var sent = await _emailService.SendEmailAsync(
            user.Email ?? string.Empty,
            "Your JOIN sign-in code",
            $"<p>Your JOIN sign-in code is <strong>{code}</strong>. It expires with your current sign-in attempt.</p>");

        var metadata = JsonSerializer.Serialize(new { sent });
        await _securityEventLogger.LogAsync(
            SecurityEventType.LoginMfaChallengeCodeSent,
            sent ? SecurityEventResult.Success : SecurityEventResult.Failure,
            user.Id,
            _currentUserService.IpAddress,
            _currentUserService.UserAgent,
            metadata,
            cancellationToken);

        return new Response<bool>
        {
            IsSuccess = sent,
            Message = sent ? "Verification code sent." : "Unable to send the verification code right now.",
            Data = true
        };
    }

    /// <summary>
    /// Generates a zero-padded numeric code of the supplied length (default 6 digits).
    /// </summary>
    private static string GenerateNumericCode(int length)
    {
        Span<byte> buffer = stackalloc byte[4];
        RandomNumberGenerator.Fill(buffer);
        var value = BitConverter.ToUInt32(buffer) % (uint)Math.Pow(10, length);
        return value.ToString("D" + length);
    }
}
