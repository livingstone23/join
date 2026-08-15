using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Commands.RequestPhoneVerification;

/// <summary>
/// Mints and dispatches a fresh 6-digit phone-verification code. Persists the hash via the
/// Dapper-backed repository, dispatches the plaintext via <see cref="ISmsService"/>, and
/// emits a <c>PhoneVerificationRequested</c> audit row.
/// </summary>
/// <param name="currentUserService">Resolves the calling user + audit context.</param>
/// <param name="phoneCodeRepository">Persistence for the verification row.</param>
/// <param name="smsService">Outbound SMS adapter (NoOp today, Twilio in SPEC 30).</param>
/// <param name="securityEventLogger">Audit logger.</param>
public sealed class RequestPhoneVerificationCommandHandler(
    ICurrentUserService currentUserService,
    IPhoneVerificationCodeRepository phoneCodeRepository,
    ISmsService smsService,
    ISecurityEventLogger securityEventLogger)
    : IRequestHandler<RequestPhoneVerificationCommand, Response<bool>>
{
    private const int CodeLifetimeMinutes = 10;
    private const int CodeNumericLength = 6;

    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly IPhoneVerificationCodeRepository _phoneCodeRepository = phoneCodeRepository;
    private readonly ISmsService _smsService = smsService;
    private readonly ISecurityEventLogger _securityEventLogger = securityEventLogger;

    public async Task<Response<bool>> Handle(RequestPhoneVerificationCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var callerUserId) || callerUserId == Guid.Empty)
        {
            return Response<bool>.Error("USER_NOT_FOUND", ["The authenticated user is not available."]);
        }

        var phoneNumber = request.PhoneNumber?.Trim() ?? string.Empty;
        // Defense in depth: the FluentValidation validator already enforces the E.164
        // shape, but if the handler is invoked outside the pipeline we still need to gate
        // against bogus input.
        if (!E164PhoneRegex.IsMatch(phoneNumber))
        {
            return Response<bool>.Error(
                "PHONE_INVALID_FORMAT",
                ["The phone number must be in E.164 format (+[country code]...)."]);
        }

        var utcNow = DateTime.UtcNow;

        // Invalidate any prior codes so only one outstanding code lives per user.
        await _phoneCodeRepository.InvalidateActiveByUserAsync(callerUserId, utcNow, cancellationToken);

        var code = GenerateNumericCode(CodeNumericLength);

        var entity = new PhoneVerificationCode
        {
            UserId = callerUserId,
            PhoneNumber = phoneNumber,
            CodeHash = JOIN.Application.Common.RecoveryCodeHasher.Hash(code), // shares the PBKDF2 helper.
            ExpiresAtUtc = utcNow.AddMinutes(CodeLifetimeMinutes),
            AttemptCount = 0,
            Created = utcNow
        };

        await _phoneCodeRepository.InsertAsync(entity, cancellationToken);

        var sent = await _smsService.SendSmsAsync(
            phoneNumber,
            $"Your JOIN verification code: {code}. Expires in {CodeLifetimeMinutes} minutes.",
            cancellationToken);

        var metadata = JsonSerializer.Serialize(new
        {
            smsDispatched = sent,
            expiresAtUtc = entity.ExpiresAtUtc,
            phoneNumber // PII; audit row stores it for fraud correlation. (Encryption hooks land in SPEC 30.)
        });
        await _securityEventLogger.LogAsync(
            SecurityEventType.PhoneVerificationRequested,
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

    /// <summary>
    /// Compiled once on first use to gate the phone format from inside the handler
    /// (the FluentValidation validator on the request also enforces the same shape).
    /// </summary>
    private static readonly Regex E164PhoneRegex = new(@"^\+[1-9]\d{1,14}$", RegexOptions.Compiled);
}
