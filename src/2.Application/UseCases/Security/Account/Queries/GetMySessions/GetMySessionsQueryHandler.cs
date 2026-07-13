using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Queries.GetMySessions;

public sealed class GetMySessionsQueryHandler(ISqlConnectionFactory connectionFactory)
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

        if (sessions.Count > 0)
        {
            sessions[0] = sessions[0] with { IsCurrent = true };
        }

        return new Response<IReadOnlyCollection<ActiveSessionDto>>
        {
            IsSuccess = true,
            Message = "Active sessions retrieved successfully.",
            Data = sessions
        };
    }
}
