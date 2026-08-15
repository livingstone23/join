using System.Text.Json;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Commands.RevokeOtherMySessions;

/// <summary>
/// Bulk-revokes every session belonging to the calling user except the live refresh token.
/// Emits a <see cref="SecurityEventType.SessionsRevokedOthers"/> audit row regardless of how
/// many rows were touched (zero rows still indicates intent).
/// </summary>
/// <param name="currentUserService">Resolves the calling user, current refresh token id, ip, and user-agent.</param>
/// <param name="sessionRepository">Dapper-backed bulk soft-revoke primitives.</param>
/// <param name="securityEventLogger">Audit-trail logger (SPEC 26 F5, brought forward in F2).</param>
public sealed class RevokeOtherMySessionsCommandHandler(
    ICurrentUserService currentUserService,
    IRoleUserSessionRepository sessionRepository,
    ISecurityEventLogger securityEventLogger)
    : IRequestHandler<RevokeOtherMySessionsCommand, Response<RevokeOtherMySessionsResponseDto>>
{
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly IRoleUserSessionRepository _sessionRepository = sessionRepository;
    private readonly ISecurityEventLogger _securityEventLogger = securityEventLogger;

    /// <inheritdoc />
    public async Task<Response<RevokeOtherMySessionsResponseDto>> Handle(
        RevokeOtherMySessionsCommand request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var callerUserId) || callerUserId == Guid.Empty)
        {
            return Response<RevokeOtherMySessionsResponseDto>.Error(
                "USER_NOT_FOUND",
                ["The authenticated user is not available."]);
        }

        // Spec: when the JWT was issued before SPEC 26 the refresh_token_id claim is absent.
        // Treat null as "current is unknown" and still attempt to revoke ALL active tokens;
        // the clause `Id <> '00000000-0000-0000-0000-000000000000'` ensures the predicate is
        // a no-op when no current row is identified, and ISNULL preserves the comparison.
        var currentRefreshTokenId = _currentUserService.RefreshTokenId;
        var utcNow = DateTime.UtcNow;

        var revokedConnections = await _sessionRepository
            .SoftRevokeActiveConnectionsAsync(callerUserId, utcNow, cancellationToken);

        var revokedTokens = await _sessionRepository
            .SoftRevokeRefreshTokensExceptAsync(callerUserId, currentRefreshTokenId, utcNow, cancellationToken);

        var metadata = JsonSerializer.Serialize(new
        {
            revokedConnections,
            revokedTokens,
            currentRefreshTokenKnown = currentRefreshTokenId.HasValue
        });

        await _securityEventLogger.LogAsync(
            SecurityEventType.SessionsRevokedOthers,
            SecurityEventResult.Success,
            callerUserId,
            _currentUserService.IpAddress,
            _currentUserService.UserAgent,
            metadata,
            cancellationToken);

        return new Response<RevokeOtherMySessionsResponseDto>
        {
            IsSuccess = true,
            Message = revokedConnections == 0 && revokedTokens == 0
                ? "No other active sessions to revoke."
                : "Other sessions revoked.",
            Data = new RevokeOtherMySessionsResponseDto
            {
                RevokedConnections = revokedConnections,
                RevokedTokens = revokedTokens
            }
        };
    }
}
