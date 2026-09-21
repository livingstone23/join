using System.Security.Cryptography;
using System.Text.Json;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Account.Commands.EmailOtpSendCode;

/// <summary>
/// Mints and dispatches a fresh 6-digit email-OTP enable/disable code for the caller. Persists
/// the hash via the Dapper-backed repository, dispatches the plaintext via
/// <see cref="IEmailService"/>, and emits an <c>EmailOtpEnableCodeRequested</c> audit row.
/// </summary>
/// <param name="userManager">Identity manager used to resolve the caller and their confirmed email.</param>
/// <param name="currentUserService">Resolves the calling user + audit context.</param>
/// <param name="emailOtpCodeRepository">Persistence for the code row.</param>
/// <param name="emailService">Email adapter (SendGrid).</param>
/// <param name="securityEventLogger">Audit logger.</param>
public sealed class EmailOtpSendCodeCommandHandler(
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUserService,
    IEmailOtpEnableCodeRepository emailOtpCodeRepository,
    IEmailService emailService,
    ISecurityEventLogger securityEventLogger)
    : IRequestHandler<EmailOtpSendCodeCommand, Response<bool>>
{
    private const int CodeLifetimeMinutes = 10;
    private const int CodeNumericLength = 6;

    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly IEmailOtpEnableCodeRepository _emailOtpCodeRepository = emailOtpCodeRepository;
    private readonly IEmailService _emailService = emailService;
    private readonly ISecurityEventLogger _securityEventLogger = securityEventLogger;

    public async Task<Response<bool>> Handle(EmailOtpSendCodeCommand request, CancellationToken cancellationToken)
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

        if (!user.EmailConfirmed)
        {
            return Response<bool>.Error("EMAIL_NOT_CONFIRMED", ["The account email must be confirmed before enabling email OTP."]);
        }

        var utcNow = DateTime.UtcNow;

        // Invalidate any prior codes so only one outstanding code lives per user.
        await _emailOtpCodeRepository.InvalidateActiveByUserAsync(callerUserId, utcNow, cancellationToken);

        var code = GenerateNumericCode(CodeNumericLength);
        var email = user.Email ?? string.Empty;

        var entity = new EmailOtpEnableCode
        {
            UserId = callerUserId,
            Email = email,
            CodeHash = RecoveryCodeHasher.Hash(code),
            ExpiresAtUtc = utcNow.AddMinutes(CodeLifetimeMinutes),
            AttemptCount = 0,
            Created = utcNow
        };

        await _emailOtpCodeRepository.InsertAsync(entity, cancellationToken);

        var sent = await _emailService.SendEmailAsync(
            email,
            "Your JOIN email OTP code",
            $"<p>Your JOIN email OTP code is <strong>{code}</strong>. It expires in {CodeLifetimeMinutes} minutes.</p>");

        var metadata = JsonSerializer.Serialize(new
        {
            sent,
            expiresAtUtc = entity.ExpiresAtUtc
        });
        await _securityEventLogger.LogAsync(
            SecurityEventType.EmailOtpEnableCodeRequested,
            sent ? SecurityEventResult.Success : SecurityEventResult.Failure,
            callerUserId,
            _currentUserService.IpAddress,
            _currentUserService.UserAgent,
            metadata,
            cancellationToken);

        return new Response<bool>
        {
            IsSuccess = sent,
            Message = sent
                ? "Verification code sent."
                : "Unable to send the verification code right now.",
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
