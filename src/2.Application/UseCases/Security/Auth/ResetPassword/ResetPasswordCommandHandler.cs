using JOIN.Application.Common;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Auth.ResetPassword;

/// <summary>
/// Resets a user password using an Identity password-reset token. Returns
/// <c>INVALID_TOKEN</c> for both unknown emails and bad tokens so callers cannot
/// enumerate which accounts exist.
/// </summary>
/// <param name="userManager">ASP.NET Identity manager.</param>
public sealed class ResetPasswordCommandHandler(
    UserManager<ApplicationUser> userManager)
    : IRequestHandler<ResetPasswordCommand, Response<bool>>
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;

    public async Task<Response<bool>> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null || !user.IsActive || user.GcRecord != 0)
        {
            return Response<bool>.Error("INVALID_TOKEN", ["The reset token is invalid or has expired."]);
        }

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description).ToArray();
            return Response<bool>.Error("INVALID_TOKEN", errors.Length == 0 ? ["The reset token is invalid or has expired."] : errors);
        }

        return new Response<bool> { IsSuccess = true, Message = "Password has been reset.", Data = true };
    }
}
