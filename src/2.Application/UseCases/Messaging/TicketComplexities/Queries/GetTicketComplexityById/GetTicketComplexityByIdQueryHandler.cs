using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketComplexities.Queries;

/// <summary>
/// Handles ticket complexity detail queries using Dapper for high-performance reads.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
public sealed class GetTicketComplexityByIdQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetTicketComplexityByIdQuery, Response<TicketComplexityDto>>
{
    /// <summary>
    /// Retrieves a ticket complexity catalog item by its unique identifier.
    /// </summary>
    public async Task<Response<TicketComplexityDto>> Handle(GetTicketComplexityByIdQuery request, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                tc.Id,
                tc.Name,
                tc.Description,
                tc.Code,
                tc.ResolutionTimeUnits,
                tc.TimeUnitId,
                tc.IsActive,
                tc.Created AS CreatedAt
            FROM Messaging.TicketComplexities tc
            WHERE tc.Id = @Id
              AND tc.GcRecord = 0;
            """;

        var ticketComplexity = await connection.QuerySingleOrDefaultAsync<TicketComplexityDto>(
            new CommandDefinition(sql, new { request.Id }, cancellationToken: cancellationToken));

        if (ticketComplexity is null)
        {
            return Response<TicketComplexityDto>.Error("TICKET_COMPLEXITY_NOT_FOUND", ["Ticket complexity not found."]);
        }

        return new Response<TicketComplexityDto>
        {
            IsSuccess = true,
            Message = "Ticket complexity retrieved successfully.",
            Data = ticketComplexity
        };
    }
}
