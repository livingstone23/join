using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Workspaces;
using MediatR;



namespace JOIN.Application.UseCases.Security.Workspaces.Commands.SwitchCompany;



/// <summary>
/// Represents the command used to switch the authenticated user's active company context and request a renewed JWT payload.
/// The corresponding handler must generate tokens through <c>ITokenService</c> or <c>IJwtProvider</c>
/// and must not instantiate <c>JwtSecurityTokenHandler</c> directly in the Application layer.
/// </summary>
/// <param name="UserId">The unique identifier of the authenticated user extracted from claims.</param>
/// <param name="CompanyId">The target company identifier to set as active context.</param>
public sealed record SwitchCompanyCommand(Guid UserId, Guid CompanyId) : IRequest<Response<SwitchCompanyResponseDto>>;