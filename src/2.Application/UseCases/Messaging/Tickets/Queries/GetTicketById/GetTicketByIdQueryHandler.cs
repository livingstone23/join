using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Queries;

/// <summary>
/// Handles ticket detail queries using Dapper for high-performance reads.
/// </summary>
public sealed class GetTicketByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetTicketByIdQuery, Response<TicketDto>>
{
    /// <summary>
    /// Retrieves a flattened ticket projection by identifier, constrained by tenant.
    /// </summary>
    public async Task<Response<TicketDto>> Handle(GetTicketByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<TicketDto>.Error("COMPANY_REQUIRED", ["The X-Company-Id header is required."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                t.Id,
                t.CompanyId,
                t.Code,
                t.Name,
                t.Description,
                t.EstimatedTime,
                t.ConsumedTime,
                t.IsVisibleToExternals,
                t.TicketStatusId,
                ts.Name AS TicketStatusName,
                t.TicketComplexityId,
                tc.Name AS TicketComplexityName,
                t.TimeUnitId,
                tu.Name AS TimeUnitName,
                t.CustomerId,
                CASE
                    WHEN c.Id IS NULL THEN NULL
                    WHEN c.CommercialName IS NOT NULL AND c.CommercialName <> '' THEN c.CommercialName
                    ELSE CONCAT(c.FirstName, ' ', COALESCE(c.MiddleName, ''), ' ', COALESCE(c.LastName, ''), ' ', COALESCE(c.SecondLastName, ''))
                END AS CustomerName,
                t.ProjectId,
                p.Name AS ProjectName,
                t.AreaId,
                a.Name AS AreaName,
                t.ChannelId,
                ch.Name AS ChannelName,
                t.CreatedByUserId,
                CONCAT(cu.FirstName, ' ', cu.LastName) AS CreatedByUserName,
                t.AssignedToUserId,
                CASE
                    WHEN au.Id IS NULL THEN NULL
                    ELSE CONCAT(au.FirstName, ' ', au.LastName)
                END AS AssignedToUserName,
                t.PrecedentTicketId,
                pt.Code AS PrecedentTicketCode,
                t.Created AS CreatedAt
            FROM Messaging.Tickets t
            INNER JOIN Messaging.TicketStatuses ts ON t.TicketStatusId = ts.Id
            INNER JOIN Messaging.TicketComplexities tc ON t.TicketComplexityId = tc.Id
            INNER JOIN Messaging.TimeUnits tu ON t.TimeUnitId = tu.Id
            LEFT JOIN Admin.Customers c ON t.CustomerId = c.Id
            LEFT JOIN Admin.Projects p ON t.ProjectId = p.Id
            LEFT JOIN Admin.Areas a ON t.AreaId = a.Id
            INNER JOIN Common.CommunicationChannels ch ON t.ChannelId = ch.Id
            INNER JOIN Security.Users cu ON t.CreatedByUserId = cu.Id
            LEFT JOIN Security.Users au ON t.AssignedToUserId = au.Id
            LEFT JOIN Messaging.Tickets pt ON t.PrecedentTicketId = pt.Id
            WHERE t.Id = @Id
              AND t.CompanyId = @TenantId
              AND t.GcRecord = 0;

            SELECT
                tl.Id,
                CASE tl.LogType
                    WHEN 0 THEN 'Creation'
                    WHEN 1 THEN 'StatusChange'
                    WHEN 2 THEN 'InternalNote'
                    WHEN 3 THEN 'ExternalNote'
                    WHEN 4 THEN 'Reassignment'
                    ELSE CONCAT('Unknown(', tl.LogType, ')')
                END AS LogType,
                tl.Summary,
                tl.Created AS CreatedAt,
                CONCAT(usr.FirstName, ' ', usr.LastName) AS UserRegisteredName,
                ps.Name AS PreviousStatusName,
                ns.Name AS NewStatusName,
                tl.ConsumedTime
            FROM Support.TicketLogs tl
            LEFT JOIN Security.Users usr ON tl.UserRegisterLogId = usr.Id
            LEFT JOIN Messaging.TicketStatuses ps ON tl.PreviousStatusId = ps.Id
            LEFT JOIN Messaging.TicketStatuses ns ON tl.TicketStatusId = ns.Id
            WHERE tl.TicketId = @Id
              AND tl.CompanyId = @TenantId
              AND tl.GcRecord = 0
            ORDER BY tl.Created DESC;
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(
                sql,
                new { request.Id, TenantId = currentUserService.CompanyId },
                cancellationToken: cancellationToken));

        var ticket = await multi.ReadFirstOrDefaultAsync<TicketDto>();

        if (ticket is null)
        {
            return Response<TicketDto>.Error("TICKET_NOT_FOUND", ["Ticket not found for the current company."]);
        }

        ticket.Logs = (await multi.ReadAsync<TicketLogDto>()).AsList();

        return new Response<TicketDto>
        {
            IsSuccess = true,
            Message = "Ticket retrieved successfully.",
            Data = ticket
        };
    }
}
