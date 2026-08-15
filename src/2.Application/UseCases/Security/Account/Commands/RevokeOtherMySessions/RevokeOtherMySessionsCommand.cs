using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Commands.RevokeOtherMySessions;

/// <summary>
/// Backs <c>POST /api/v1/account/sessions/revoke-others</c>: closes every active session for the
/// calling user except the one that produced the current request.
/// </summary>
public sealed record RevokeOtherMySessionsCommand : IRequest<Response<RevokeOtherMySessionsResponseDto>>;
