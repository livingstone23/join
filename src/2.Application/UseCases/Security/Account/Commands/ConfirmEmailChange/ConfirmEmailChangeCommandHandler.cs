using System.Text.Json;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Account.Commands.ConfirmEmailChange;

/// <summary>
/// Confirms a pending email change using Identity's built-in
/// <c>AspNetUserTokens</c>-backed verify path. Emits an audit row regardless of
/// outcome (<c>EmailChangeConfirmed</c> on success, <c>EmailChangeFailed</c> on rejection).
/// </summary>
/// <param name="userManager">ASP.NET Core Identity manager used to load and mutate the user.</param>
/// <param name="currentUserService">Resolves the calling user + audit context.</param>
/// <param name="securityEventLogger">Audit logger.</param>
public sealed class ConfirmEmailChangeCommandHandler(
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUserService,
    ISecurityEventLogger securityEventLogger)
    : IRequestHandler<ConfirmEmailChangeCommand, Response<bool>>
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly ISecurityEventLogger _securityEventLogger = securityEventLogger;

    public async Task<Response<bool>> Handle(ConfirmEmailChangeCommand request, CancellationToken cancellationToken)
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

        var token = request.Token?.Trim() ?? string.Empty;
        var newEmail = request.NewEmail?.Trim() ?? string.Empty;

        var result = await _userManager.ChangeEmailAsync(user, newEmail, token);
        if (!result.Succeeded)
        {
            await LogAsync(SecurityEventType.EmailChangeFailed, callerUserId, "identity_rejected",
                new { reason = string.Join("; ", result.Errors.Select(e => e.Description)) },
                cancellationToken);
            return Response<bool>.Error(
                "EMAIL_CHANGE_FAILED",
                result.Errors.Select(error => error.Description).ToArray());
        }

        // Identity flips EmailConfirmed in tandem with ChangeEmail — only when the user
        // already had EmailConfirmed=true. Mirror that flag so the contract holds.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);
        }

        await LogAsync(SecurityEventType.EmailChangeConfirmed, callerUserId, "ok",
            new { newEmail }, cancellationToken);

        return new Response<bool>
        {
            IsSuccess = true,
            Message = "Email address changed.",
            Data = true
        };
    }

    private Task LogAsync(
        SecurityEventType eventType,
        Guid userId,
        string reason,
        object metadata,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            reason,
            metadata
        });
        return _securityEventLogger.LogAsync(
            eventType,
            SecurityEventResult.Success,
            userId,
            _currentUserService.IpAddress,
            _currentUserService.UserAgent,
            payload,
            cancellationToken);
    }
}
