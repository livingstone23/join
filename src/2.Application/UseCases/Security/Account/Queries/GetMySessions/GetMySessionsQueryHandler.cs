using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Queries.GetMySessions;

public sealed class GetMySessionsQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetMySessionsQuery, Response<IReadOnlyCollection<ActiveSessionDto>>>
{
    public async Task<Response<IReadOnlyCollection<ActiveSessionDto>>> Handle(GetMySessionsQuery request, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                SessionId,
                ConnectedAtUtc,
                LastActivityAtUtc,
                Device,
                IpAddress
            FROM (
                SELECT
                    ucl.Id AS SessionId,
                    ucl.ConnectionDate AS ConnectedAtUtc,
                    COALESCE(ucl.DisconnectionDate, ucl.ConnectionDate) AS LastActivityAtUtc,
                    ucl.UserAgent AS Device,
                    ucl.IpAddress
                FROM Security.UserConnectionLogs ucl
                WHERE ucl.UserId = @UserId
                  AND ucl.IsActiveSession = 1

                UNION ALL

                SELECT
                    urt.Id AS SessionId,
                    urt.Created AS ConnectedAtUtc,
                    COALESCE(urt.LastModified, urt.Created) AS LastActivityAtUtc,
                    N'JWT Refresh Token' AS Device,
                    CAST(NULL AS NVARCHAR(45)) AS IpAddress
                FROM Security.UserRefreshTokens urt
                WHERE urt.UserId = @UserId
                  AND urt.IsRevoked = 0
                  AND urt.ExpiryDate > @UtcNow
                  AND urt.GcRecord = 0
            ) AS sessions
            ORDER BY LastActivityAtUtc DESC;
            """;

        var sessions = (await connection.QueryAsync<ActiveSessionDto>(
            new CommandDefinition(
                sql,
                new { request.UserId, UtcNow = DateTime.UtcNow },
                cancellationToken: cancellationToken))).AsList();

        // The row that authenticated *this* request is the one whose SessionId matches the
        // refresh_token_id claim of the caller's own JWT — not simply "whichever row was most
        // recently active" (that heuristic breaks as soon as another of the user's own sessions,
        // e.g. a second browser, was used more recently than this one).
        var currentRefreshTokenId = currentUserService.RefreshTokenId;
        var currentIndex = currentRefreshTokenId.HasValue
            ? sessions.FindIndex(s => s.SessionId == currentRefreshTokenId.Value)
            : -1;

        if (currentIndex < 0 && sessions.Count > 0)
        {
            // Back-compat fallback for tokens issued before the refresh_token_id claim existed.
            currentIndex = 0;
        }

        if (currentIndex >= 0)
        {
            sessions[currentIndex] = sessions[currentIndex] with { IsCurrent = true };
        }

        return new Response<IReadOnlyCollection<ActiveSessionDto>>
        {
            IsSuccess = true,
            Message = "Active sessions retrieved successfully.",
            Data = sessions
        };
    }
}
