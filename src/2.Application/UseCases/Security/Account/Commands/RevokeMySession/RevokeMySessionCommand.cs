using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Commands.RevokeMySession;

/// <summary>
/// Revokes a single session identified by either a <c>Security.UserConnectionLogs</c> or a
/// <c>Security.UserRefreshTokens</c> row id. Backs <c>DELETE /api/v1/account/sessions/{sessionId}</c>.
/// Cross-user attempts fail with <c>SESSION_NOT_FOUND</c>; revoking the live refresh token
/// fails with <c>CANNOT_REVOKE_CURRENT</c>.
/// </summary>
public sealed record RevokeMySessionCommand(Guid SessionId) : IRequest<Response<RevokeMySessionResponseDto>>;
