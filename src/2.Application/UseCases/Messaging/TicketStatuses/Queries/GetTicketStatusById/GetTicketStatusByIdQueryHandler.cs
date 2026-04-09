using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketStatuses.Queries;

/// <summary>
/// Handles ticket status detail queries using Dapper for high-performance reads.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
public sealed class GetTicketStatusByIdQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetTicketStatusByIdQuery, Response<TicketStatusDto>>
{
    /// <summary>
    /// Retrieves a ticket status catalog item by its unique identifier.
    /// </summary>
    public async Task<Response<TicketStatusDto>> Handle(GetTicketStatusByIdQuery request, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                ts.Id,
                ts.Name,
                ts.Description,
                ts.Code,
                ts.IsActive,
                ts.Created AS CreatedAt
            FROM Messaging.TicketStatuses ts
            WHERE ts.Id = @Id
              AND ts.GcRecord = 0;
            """;

        var ticketStatus = await connection.QuerySingleOrDefaultAsync<TicketStatusDto>(
            new CommandDefinition(sql, new { request.Id }, cancellationToken: cancellationToken));

        if (ticketStatus is null)
        {
            return Response<TicketStatusDto>.Error("TICKET_STATUS_NOT_FOUND", ["Ticket status not found."]);
        }

        return new Response<TicketStatusDto>
        {
            IsSuccess = true,
            Message = "Ticket status retrieved successfully.",
            Data = ticketStatus
        };
    }
}
