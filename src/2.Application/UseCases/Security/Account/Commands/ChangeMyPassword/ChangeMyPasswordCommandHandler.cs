using JOIN.Application.Common;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Account.Commands.ChangeMyPassword;

public sealed class ChangeMyPasswordCommandHandler(UserManager<ApplicationUser> userManager)
    : IRequestHandler<ChangeMyPasswordCommand, Response<bool>>
{
    public async Task<Response<bool>> Handle(ChangeMyPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null || !user.IsActive || user.GcRecord != 0)
        {
            return Response<bool>.Error("ACCOUNT_NOT_FOUND", ["Authenticated account was not found."]);
        }

        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return Response<bool>.Error("PASSWORD_CONFIRMATION_MISMATCH", ["New password and confirmation do not match."]);
        }

        var result = await userManager.ChangePasswordAsync(user, request.OldPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return Response<bool>.Error(
                "PASSWORD_CHANGE_FAILED",
                result.Errors.Select(error => error.Description).ToArray());
        }

        return new Response<bool>
        {
            IsSuccess = true,
            Message = "Password changed successfully.",
            Data = true
        };
    }
}
