using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Account.Commands.UpdateMyProfile;

public sealed class UpdateMyProfileCommandHandler(
    UserManager<ApplicationUser> userManager)
    : IRequestHandler<UpdateMyProfileCommand, Response<AccountProfileResponseDto>>
{
    public async Task<Response<AccountProfileResponseDto>> Handle(UpdateMyProfileCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null || !user.IsActive || user.GcRecord != 0)
        {
            return Response<AccountProfileResponseDto>.Error("ACCOUNT_NOT_FOUND", ["Authenticated account was not found."]);
        }

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return Response<AccountProfileResponseDto>.Error(
                "PROFILE_UPDATE_FAILED",
                updateResult.Errors.Select(error => error.Description).ToArray());
        }

        var responseDto = new AccountProfileResponseDto
        {
            UserName = user.UserName ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email ?? string.Empty,
            AvatarUrl = user.AvatarUrl,
            PhoneNumber = user.PhoneNumber,
            EmailConfirmed = user.EmailConfirmed,
            PhoneNumberConfirmed = user.PhoneNumberConfirmed,
            IsMfaEnabled = user.IsMfaEnabled,
            IsSuperAdmin = user.IsSuperAdmin,
            IsSuperAdminCompany = user.IsSuperAdminCompany,
            Created = user.Created,
            CommunicationChannels = Array.Empty<CommunicationChannelDto>()
        };

        return new Response<AccountProfileResponseDto>
        {
            IsSuccess = true,
            Message = "Profile updated successfully.",
            Data = responseDto
        };
    }
}
