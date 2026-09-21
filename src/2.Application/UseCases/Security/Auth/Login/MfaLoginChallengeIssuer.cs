using System.Security.Cryptography;
using System.Text.Json;
using JOIN.Application.Common;
using JOIN.Application.Common.Options;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;
using Microsoft.Extensions.Options;



namespace JOIN.Application.UseCases.Security.Auth.Login;



/// <summary>
/// Creates the server-side <see cref="MfaLoginChallenge"/> and shapes the challenge branch of
/// <see cref="LoginResponse"/> when a user has 1+ active 2FA method. Counterpart to
/// <see cref="AuthenticatedSessionIssuer"/> for the "login stops here" path — no company/role
/// resolution happens here, that is re-evaluated later by <c>VerifyMfaChallengeCommandHandler</c>.
/// </summary>
/// <param name="challengeRepository">Persistence for the login-challenge row.</param>
/// <param name="mfaOptions">Bound <see cref="MfaOptions"/> (challenge expiration window).</param>
/// <param name="currentUserService">Resolves the caller's IP/user-agent for audit logging.</param>
/// <param name="securityEventLogger">Records the <c>LoginMfaRequired</c> security event.</param>
public sealed class MfaLoginChallengeIssuer(
    IMfaLoginChallengeRepository challengeRepository,
    IOptions<MfaOptions> mfaOptions,
    ICurrentUserService currentUserService,
    ISecurityEventLogger securityEventLogger)
    : IMfaLoginChallengeIssuer
{
    private const int OpaqueTokenBytes = 32;

    private readonly IMfaLoginChallengeRepository _challengeRepository = challengeRepository;
    private readonly MfaOptions _mfaOptions = mfaOptions.Value;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly ISecurityEventLogger _securityEventLogger = securityEventLogger;

    /// <inheritdoc />
    public async Task<LoginResponse> IssueChallengeAsync(ApplicationUser user, Guid? targetCompanyId, CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        var availableMethods = MfaMethodResolver.ResolveAvailable(user);

        var opaqueToken = GenerateOpaqueToken();
        var challenge = new MfaLoginChallenge
        {
            UserId = user.Id,
            TargetCompanyId = targetCompanyId,
            TokenHash = OpaqueTokenHasher.Hash(opaqueToken),
            ExpiresAtUtc = utcNow.AddMinutes(_mfaOptions.ChallengeExpirationMinutes),
            AttemptCount = 0,
            Created = utcNow
        };

        await _challengeRepository.InsertAsync(challenge, cancellationToken);

        var metadata = JsonSerializer.Serialize(new
        {
            availableMethods,
            preferredMethod = user.PreferredMfaMethod
        });
        await _securityEventLogger.LogAsync(
            SecurityEventType.LoginMfaRequired,
            SecurityEventResult.Success,
            user.Id,
            _currentUserService.IpAddress,
            _currentUserService.UserAgent,
            metadata,
            cancellationToken);

        return new LoginResponse
        {
            UserId = user.Id,
            UserName = user.UserName ?? user.Email ?? string.Empty,
            Email = user.Email ?? string.Empty,
            CompanyId = null,
            Roles = [],
            Token = null,
            RefreshToken = null,
            Expiration = null,
            ChallengeToken = opaqueToken,
            AvailableMethods = availableMethods,
            PreferredMethod = user.PreferredMfaMethod
        };
    }

    /// <summary>
    /// Generates a cryptographically secure opaque token returned to the client as
    /// <c>ChallengeToken</c>. Only its deterministic hash (<see cref="OpaqueTokenHasher"/>) is persisted.
    /// </summary>
    private static string GenerateOpaqueToken()
    {
        Span<byte> buffer = stackalloc byte[OpaqueTokenBytes];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer);
    }
}
