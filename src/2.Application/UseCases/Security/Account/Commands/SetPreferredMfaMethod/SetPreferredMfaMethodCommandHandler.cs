using System.Text.Json;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Account.Commands.SetPreferredMfaMethod;

/// <summary>
/// Persists <c>PreferredMfaMethod</c> on the caller. Rejects a method that is unknown or not
/// currently active for them (<c>PREFERRED_METHOD_NOT_ENABLED</c>) — derived from the same
/// <see cref="MfaMethodResolver"/> source of truth used by the login challenge.
/// </summary>
public sealed class SetPreferredMfaMethodCommandHandler(
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUserService,
    ISecurityEventLogger securityEventLogger)
    : IRequestHandler<SetPreferredMfaMethodCommand, Response<bool>>
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly ISecurityEventLogger _securityEventLogger = securityEventLogger;

    public async Task<Response<bool>> Handle(SetPreferredMfaMethodCommand request, CancellationToken cancellationToken)
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

        var method = request.Method?.Trim().ToLowerInvariant() ?? string.Empty;
        var availableMethods = MfaMethodResolver.ResolveAvailable(user);
        if (!availableMethods.Contains(method))
        {
            return Response<bool>.Error(
                "PREFERRED_METHOD_NOT_ENABLED",
                ["The requested method is not active for this user."]);
        }

        user.PreferredMfaMethod = method;
        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return Response<bool>.Error(
                "USER_UPDATE_FAILED",
                updateResult.Errors.Select(error => error.Description).ToArray());
        }

        var metadata = JsonSerializer.Serialize(new { userId = callerUserId, preferredMethod = method });
        await _securityEventLogger.LogAsync(
            SecurityEventType.MfaPreferredMethodChanged,
            SecurityEventResult.Success,
            callerUserId,
            _currentUserService.IpAddress,
            _currentUserService.UserAgent,
            metadata,
            cancellationToken);

        return new Response<bool>
        {
            IsSuccess = true,
            Message = "Preferred MFA method updated.",
            Data = true
        };
    }
}
