using System.Text.Json;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Account.Commands.SetupMfa;

/// <summary>
/// Generates an MFA enrolment bundle for the caller. Persists a fresh secret on
/// <see cref="ApplicationUser.MfaSecretKey"/> + 10 PBKDF2-hashed recovery codes, and
/// emits a <c>MfaSetupInitiated</c> audit row. Does NOT flip <c>IsMfaEnabled</c>.
/// </summary>
/// <param name="userManager">Identity manager used to mutate <c>AspNetUsers</c>.</param>
/// <param name="currentUserService">Resolves the calling user id + audit context.</param>
/// <param name="mfaTotpValidator">TOTP helpers (Base32 generation + QR URI).</param>
/// <param name="recoveryCodeRepository">Bulk recovery code persistence.</param>
/// <param name="securityEventLogger">Audit logger.</param>
public sealed class SetupMfaCommandHandler(
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUserService,
    IMfaTotpValidator mfaTotpValidator,
    IUserMfaRecoveryCodeRepository recoveryCodeRepository,
    ISecurityEventLogger securityEventLogger)
    : IRequestHandler<SetupMfaCommand, Response<SetupMfaResponseDto>>
{
    private const int RecoveryCodeCount = 10;
    private const string Issuer = "JOIN";

    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly IMfaTotpValidator _mfaTotpValidator = mfaTotpValidator;
    private readonly IUserMfaRecoveryCodeRepository _recoveryCodeRepository = recoveryCodeRepository;
    private readonly ISecurityEventLogger _securityEventLogger = securityEventLogger;

    public async Task<Response<SetupMfaResponseDto>> Handle(
        SetupMfaCommand request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var callerUserId) || callerUserId == Guid.Empty)
        {
            return Response<SetupMfaResponseDto>.Error(
                "USER_NOT_FOUND",
                ["The authenticated user is not available."]);
        }

        var user = await _userManager.FindByIdAsync(callerUserId.ToString());
        if (user is null || !user.IsActive || user.GcRecord != 0)
        {
            return Response<SetupMfaResponseDto>.Error(
                "ACCOUNT_NOT_FOUND",
                ["Authenticated account was not found."]);
        }

        // Generating a fresh secret implicitly rotates any previous setup that the user
        // never confirmed. Wipe the recovery-code set so an enrolled-but-replaced secret
        // does not leave orphan codes accessible to the new code-generation.
        var utcNow = DateTime.UtcNow;
        await _recoveryCodeRepository.DeleteAllByUserAsync(callerUserId, utcNow, cancellationToken);

        var secret = _mfaTotpValidator.GenerateSecret();
        var qrCodeUri = _mfaTotpValidator.BuildQrCodeUri(secret, user.Email ?? user.UserName ?? callerUserId.ToString(), Issuer);

        var codes = new List<UserMfaRecoveryCode>(RecoveryCodeCount);
        var plaintextCodes = new List<string>(RecoveryCodeCount);
        for (int i = 0; i < RecoveryCodeCount; i++)
        {
            var plaintext = RecoveryCodeHasher.GenerateCode();
            plaintextCodes.Add(plaintext);
            codes.Add(new UserMfaRecoveryCode
            {
                UserId = callerUserId,
                CodeHash = RecoveryCodeHasher.Hash(plaintext),
                CreatedAtUtc = utcNow,
                Created = utcNow,
                CreatedBy = user.Email ?? user.UserName
            });
        }

        // Persist recovery codes via Dapper repository, then user row via UserManager.
        await _recoveryCodeRepository.InsertManyAsync(codes, cancellationToken);

        user.MfaSecretKey = secret;
        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return Response<SetupMfaResponseDto>.Error(
                "USER_UPDATE_FAILED",
                updateResult.Errors.Select(error => error.Description).ToArray());
        }

        var metadata = JsonSerializer.Serialize(new
        {
            userId = callerUserId,
            recoveryCodeCount = RecoveryCodeCount
        });
        await _securityEventLogger.LogAsync(
            SecurityEventType.MfaSetupInitiated,
            SecurityEventResult.Success,
            callerUserId,
            _currentUserService.IpAddress,
            _currentUserService.UserAgent,
            metadata,
            cancellationToken);

        return new Response<SetupMfaResponseDto>
        {
            IsSuccess = true,
            Message = "MFA enrolment bundle generated. Confirm with /mfa/enable to activate.",
            Data = new SetupMfaResponseDto
            {
                Secret = secret,
                QrCodeUri = qrCodeUri,
                RecoveryCodes = plaintextCodes
            }
        };
    }
}
