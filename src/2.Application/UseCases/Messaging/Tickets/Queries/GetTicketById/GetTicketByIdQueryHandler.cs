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
            """;

        var ticket = await connection.QuerySingleOrDefaultAsync<TicketDto>(
            new CommandDefinition(
                sql,
                new { request.Id, TenantId = currentUserService.CompanyId },
                cancellationToken: cancellationToken));

        if (ticket is null)
        {
            return Response<TicketDto>.Error("TICKET_NOT_FOUND", ["Ticket not found for the current company."]);
        }

        return new Response<TicketDto>
        {
            IsSuccess = true,
            Message = "Ticket retrieved successfully.",
            Data = ticket
        };
    }
}
