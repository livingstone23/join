using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using JOIN.Application.Interface;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Account.Queries.GetMyProfile;

public sealed class GetMyProfileQueryHandler(
    UserManager<ApplicationUser> userManager,
    ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetMyProfileQuery, Response<AccountProfileResponseDto>>
{
    public async Task<Response<AccountProfileResponseDto>> Handle(GetMyProfileQuery request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null || !user.IsActive || user.GcRecord != 0)
        {
            return Response<AccountProfileResponseDto>.Error("ACCOUNT_NOT_FOUND", ["Authenticated account was not found."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string channelsSql = """
            SELECT
                cc.Name AS Type,
                ucc.ChannelIdentifier AS Value,
                ucc.IsPreferred
            FROM Admin.UserCommunicationChannels ucc
            INNER JOIN Common.CommunicationChannels cc
                ON cc.Id = ucc.CommunicationChannelId
            WHERE ucc.UserId = @UserId
              AND ucc.GcRecord = 0
              AND cc.GcRecord = 0;
            """;

        var channels = (await connection.QueryAsync<CommunicationChannelDto>(
            new CommandDefinition(
                channelsSql,
                new { request.UserId },
                cancellationToken: cancellationToken))).AsList();

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
            CommunicationChannels = channels
        };

        return new Response<AccountProfileResponseDto>
        {
            IsSuccess = true,
            Message = "Profile retrieved successfully.",
            Data = dto
        };
    }
}
