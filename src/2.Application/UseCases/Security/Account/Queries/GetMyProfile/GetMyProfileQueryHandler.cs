using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Account.Queries.GetMyProfile;

public sealed class GetMyProfileQueryHandler(
    UserManager<ApplicationUser> userManager,
    IUnitOfWork unitOfWork)
    : IRequestHandler<GetMyProfileQuery, Response<AccountProfileResponseDto>>
{
    public async Task<Response<AccountProfileResponseDto>> Handle(GetMyProfileQuery request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null || !user.IsActive || user.GcRecord != 0)
        {
            return Response<AccountProfileResponseDto>.Error("ACCOUNT_NOT_FOUND", ["Authenticated account was not found."]);
        }

        var allUserChannels = await unitOfWork.GetRepository<UserCommunicationChannel>().GetAllAsync();
        var userChannels = allUserChannels
            .Where(channel => channel.UserId == request.UserId)
            .ToList();

        var allCatalogChannels = await unitOfWork.GetRepository<CommunicationChannel>().GetAllAsync();
        var catalogById = allCatalogChannels.ToDictionary(channel => channel.Id, channel => channel.Name);

        var dto = new AccountProfileResponseDto
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
            CommunicationChannels = userChannels
                .Select(channel => new CommunicationChannelDto
                {
                    Type = catalogById.TryGetValue(channel.CommunicationChannelId, out var typeName)
                        ? typeName
                        : string.Empty,
                    Value = channel.ChannelIdentifier,
                    IsPreferred = channel.IsPreferred
                })
                .ToArray()
        };

        return new Response<AccountProfileResponseDto>
        {
            IsSuccess = true,
            Message = "Profile retrieved successfully.",
            Data = dto
        };
    }
}
