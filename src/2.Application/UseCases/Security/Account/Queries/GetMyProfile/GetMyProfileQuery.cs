using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using MediatR;



namespace JOIN.Application.UseCases.Security.Account.Queries.GetMyProfile;



/// <summary>
/// Represents the query used to retrieve the basic profile information for the current authenticated user.
/// </summary>
/// <param name="UserId">The unique identifier of the authenticated user extracted from claims.</param>
public sealed record GetMyProfileQuery(Guid UserId) : IRequest<Response<AccountProfileResponseDto>>;