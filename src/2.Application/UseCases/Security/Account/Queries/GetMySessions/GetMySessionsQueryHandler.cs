using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Queries.GetMySessions;

public sealed class GetMySessionsQueryHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<GetMySessionsQuery, Response<IReadOnlyCollection<ActiveSessionDto>>>
{
    public async Task<Response<IReadOnlyCollection<ActiveSessionDto>>> Handle(GetMySessionsQuery request, CancellationToken cancellationToken)
    {
        var allLogs = await unitOfWork.GetRepository<UserConnectionLog>().GetAllAsync();
        var activeLogs = allLogs
            .Where(log => log.UserId == request.UserId && log.IsActiveSession)
            .OrderByDescending(log => log.ConnectionDate)
            .ToList();

        var sessionsFromLogs = activeLogs
            .Select((log, index) => new ActiveSessionDto
            {
                SessionId = log.Id,
                ConnectedAtUtc = log.ConnectionDate,
                LastActivityAtUtc = log.DisconnectionDate ?? log.ConnectionDate,
                Device = log.UserAgent,
                IpAddress = log.IpAddress,
                IsCurrent = false
            })
            .ToList();

        // Backward-compatible fallback: old environments may not persist UserConnectionLog yet,
        // but still have active refresh tokens representing valid authenticated sessions.
        var activeRefreshTokens = (await unitOfWork.GetRepository<UserRefreshToken>().GetAllAsync())
            .Where(token => token.UserId == request.UserId && !token.IsRevoked && token.ExpiryDate > DateTime.UtcNow)
            .OrderByDescending(token => token.LastModified ?? token.Created)
            .ToList();

        var sessionsFromRefreshTokens = activeRefreshTokens
            .Select(token => new ActiveSessionDto
            {
                SessionId = token.Id,
                ConnectedAtUtc = token.Created,
                LastActivityAtUtc = token.LastModified ?? token.Created,
                Device = "JWT Refresh Token",
                IpAddress = null,
                IsCurrent = false
            })
            .ToList();

        var sessions = sessionsFromLogs
            .Concat(sessionsFromRefreshTokens)
            .OrderByDescending(session => session.LastActivityAtUtc)
            .ToList();

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
