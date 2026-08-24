using JOIN.Application.Common;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Auth.SetupPassword;

/// <summary>
/// Completes first-time account activation by setting an initial password from a setup token.
/// Marks <c>EmailConfirmed = true</c> on success. Returns <c>PASSWORD_ALREADY_SET</c> when
/// the account already has a password — setup tokens are only valid for the first activation.
/// </summary>
/// <param name="userManager">ASP.NET Identity manager.</param>
public sealed class SetupPasswordCommandHandler(
    UserManager<ApplicationUser> userManager)
    : IRequestHandler<SetupPasswordCommand, Response<bool>>
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;

    public async Task<Response<bool>> Handle(SetupPasswordCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null || !user.IsActive || user.GcRecord != 0)
        {
            return Response<bool>.Error("INVALID_TOKEN", ["The setup token is invalid or has expired."]);
        }

        if (await _userManager.HasPasswordAsync(user))
        {
            return Response<bool>.Error("PASSWORD_ALREADY_SET", ["This account already has a password. Use reset-password instead."]);
        }

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description).ToArray();
            return Response<bool>.Error("INVALID_TOKEN", errors.Length == 0 ? ["The setup token is invalid or has expired."] : errors);
        }

        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                var errors = updateResult.Errors.Select(e => e.Description).ToArray();
                return Response<bool>.Error("USER_UPDATE_FAILED", errors);
            }
        }

        return new Response<bool> { IsSuccess = true, Message = "Password has been set. Account activated.", Data = true };
    }
}
