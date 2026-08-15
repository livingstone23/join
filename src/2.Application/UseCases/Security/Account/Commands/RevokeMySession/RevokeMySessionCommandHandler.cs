using System.Text.Json;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Commands.RevokeMySession;

/// <summary>
/// Soft-revokes one session, looking it up across both <c>UserConnectionLogs</c> and
/// <c>UserRefreshTokens</c>. Emits a <see cref="SecurityEventType.SessionRevoked"/> event on success.
/// </summary>
/// <param name="currentUserService">Resolves the calling user, tenant, current refresh token id, ip, and user-agent.</param>
/// <param name="sessionRepository">Dapper-backed session lookup + soft-revoke primitives.</param>
/// <param name="securityEventLogger">Audit-trail logger (see SPEC 26 F5).</param>
public sealed class RevokeMySessionCommandHandler(
    ICurrentUserService currentUserService,
    IRoleUserSessionRepository sessionRepository,
    ISecurityEventLogger securityEventLogger)
    : IRequestHandler<RevokeMySessionCommand, Response<RevokeMySessionResponseDto>>
{
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly IRoleUserSessionRepository _sessionRepository = sessionRepository;
    private readonly ISecurityEventLogger _securityEventLogger = securityEventLogger;

    /// <inheritdoc />
    public async Task<Response<RevokeMySessionResponseDto>> Handle(
        RevokeMySessionCommand request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var callerUserId) || callerUserId == Guid.Empty)
        {
            return Response<RevokeMySessionResponseDto>.Error(
                "USER_NOT_FOUND",
                ["The authenticated user is not available."]);
        }

        // 1. Locate the row in either source table.
        var lookup = await _sessionRepository.FindActiveByIdAsync(request.SessionId, cancellationToken);
        if (lookup is null)
        {
            return Response<RevokeMySessionResponseDto>.Error(
                "SESSION_NOT_FOUND",
                ["The requested session id does not match any active session."]);
        }

        // 2. Cross-user defense: if the row belongs to someone else we deliberately return
        // SESSION_NOT_FOUND so we don't leak whether the id exists in another tenant.
        var ownerId = await _sessionRepository.GetUserIdBySessionIdAsync(request.SessionId, cancellationToken);
        if (ownerId != callerUserId)
        {
            return Response<RevokeMySessionResponseDto>.Error(
                "SESSION_NOT_FOUND",
                ["The requested session id does not match any active session."]);
        }

        // 3. Block revoking the live refresh token that produced the current request.
        var currentRefreshTokenId = _currentUserService.RefreshTokenId;
        if (lookup.Type == SessionType.UserRefreshToken
            && currentRefreshTokenId.HasValue
            && currentRefreshTokenId.Value == request.SessionId)
        {
            return Response<RevokeMySessionResponseDto>.Error(
                "CANNOT_REVOKE_CURRENT",
                ["The refresh token that authenticated the current request cannot be revoked here."]);
        }

        // 4. Apply the soft revoke in the correct table.
        var utcNow = DateTime.UtcNow;
        int affected;
        var sessionTypeLabel = lookup.Type.ToString();

        if (lookup.Type == SessionType.UserRefreshToken)
        {
            affected = await _sessionRepository.SoftRevokeSingleRefreshTokenAsync(
                lookup.Id,
                currentRefreshTokenId,
                utcNow,
                cancellationToken);
        }
        else
        {
            affected = await _sessionRepository.SoftRevokeSingleConnectionAsync(
                lookup.Id,
                utcNow,
                cancellationToken);
        }

        // The lookup told us the row is "active" but a concurrent caller may have just revoked it.
        // Treat that as success-equivalent: nothing to do, no error. We still avoid logging.
        if (affected == 0)
        {
            return new Response<RevokeMySessionResponseDto>
            {
                IsSuccess = true,
                Message = "Session was already inactive.",
                Data = new RevokeMySessionResponseDto
                {
                    RevokedConnections = 0,
                    RevokedTokens = 0,
                    SessionType = sessionTypeLabel
                }
            };
        }

        // 5. Audit trail.
        var metadata = JsonSerializer.Serialize(new
        {
            sessionId = request.SessionId,
            sessionType = sessionTypeLabel
        });
        await _securityEventLogger.LogAsync(
            SecurityEventType.SessionRevoked,
            SecurityEventResult.Success,
            callerUserId,
            _currentUserService.IpAddress,
            _currentUserService.UserAgent,
            metadata,
            cancellationToken);

        return new Response<RevokeMySessionResponseDto>
        {
            IsSuccess = true,
            Message = "Session revoked.",
            Data = new RevokeMySessionResponseDto
            {
                RevokedConnections = lookup.Type == SessionType.UserConnectionLog ? 1 : 0,
                RevokedTokens = lookup.Type == SessionType.UserRefreshToken ? 1 : 0,
                SessionType = sessionTypeLabel
            }
        };
    }
}
