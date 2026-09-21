using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;



namespace JOIN.Application.UseCases.Security.Auth.Login;



/// <summary>
/// Handles authentication requests: validates credentials, then either stops at an MFA challenge
/// (1+ active 2FA method) or delegates company/role resolution and session issuance to
/// <see cref="IAuthenticatedSessionIssuer"/>.
/// </summary>
/// <param name="userManager">ASP.NET Core Identity manager used to validate the user credentials.</param>
/// <param name="authenticatedSessionIssuer">Resolves the effective company/roles and issues the JWT/refresh token.</param>
/// <param name="mfaLoginChallengeIssuer">Creates the MFA challenge when the user has 1+ active 2FA method.</param>
/// <param name="currentUserService">Resolves the caller's IP/user-agent for audit logging.</param>
/// <param name="securityEventLogger">Records login failure security events.</param>
public class LoginCommandHandler(
    UserManager<ApplicationUser> userManager,
    IAuthenticatedSessionIssuer authenticatedSessionIssuer,
    IMfaLoginChallengeIssuer mfaLoginChallengeIssuer,
    ICurrentUserService currentUserService,
    ISecurityEventLogger securityEventLogger)
    : IRequestHandler<LoginCommand, LoginResponse>
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly IAuthenticatedSessionIssuer _authenticatedSessionIssuer = authenticatedSessionIssuer;
    private readonly IMfaLoginChallengeIssuer _mfaLoginChallengeIssuer = mfaLoginChallengeIssuer;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly ISecurityEventLogger _securityEventLogger = securityEventLogger;

    /// <summary>
    /// Authenticates the user and creates the login response payload.
    /// </summary>
    /// <param name="request">The login request payload.</param>
    /// <param name="cancellationToken">The cancellation token for the current operation.</param>
    /// <returns>The authenticated session payload.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when the credentials or tenant context are invalid.</exception>
    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim();

        var user = await _userManager.FindByEmailAsync(normalizedEmail);
        if (user is null)
        {
            await LogLoginFailureAsync(reason: "user_not_found", userId: null, attemptedEmail: normalizedEmail);
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        if (!user.IsActive || user.GcRecord != 0)
        {
            await LogLoginFailureAsync(reason: "inactive", userId: user.Id, attemptedEmail: normalizedEmail);
            throw new UnauthorizedAccessException("The user account is inactive.");
        }

        var passwordIsValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordIsValid)
        {
            await LogLoginFailureAsync(reason: "password_mismatch", userId: user.Id, attemptedEmail: normalizedEmail);
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        if (user.IsMfaEnabled || user.IsEmailOtpEnabled)
        {
            return await _mfaLoginChallengeIssuer.IssueChallengeAsync(user, request.TargetCompanyId, cancellationToken);
        }

        return await _authenticatedSessionIssuer.IssueAsync(user, request.TargetCompanyId, cancellationToken);
    }

    /// <summary>
    /// Records a LoginFailed audit row for the supplied rejection reason.
    /// Best-effort: failures inside <see cref="ISecurityEventLogger"/> are swallowed by
    /// the logger itself, so this method never throws.
    /// </summary>
    private async Task LogLoginFailureAsync(string reason, Guid? userId, string attemptedEmail)
    {
        var metadata = System.Text.Json.JsonSerializer.Serialize(new
        {
            reason,
            attemptedEmail
        });
        await _securityEventLogger.LogAsync(
            SecurityEventType.LoginFailed,
            SecurityEventResult.Failure,
            userId,
            _currentUserService.IpAddress,
            _currentUserService.UserAgent,
            metadata);
    }
}
