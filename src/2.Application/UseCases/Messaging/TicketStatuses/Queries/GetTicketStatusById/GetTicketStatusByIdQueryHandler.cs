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
public sealed class GetTicketStatusByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetTicketStatusByIdQuery, Response<TicketStatusDto>>
{
    /// <summary>
    /// Retrieves a ticket status catalog item by its unique identifier.
    /// </summary>
    public async Task<Response<TicketStatusDto>> Handle(GetTicketStatusByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<TicketStatusDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                ts.Id,
                ts.CompanyId,
                c.Name AS CompanyName,
                ts.Name,
                ts.Description,
                ts.Code,
                ts.IsActive,
                ts.IsInitial,
                ts.IsPaused,
                ts.IsFinal,
                ts.Created AS CreatedAt
            FROM Messaging.TicketStatuses ts
            INNER JOIN Common.Companies c ON c.Id = ts.CompanyId
            WHERE ts.Id = @Id
              AND ts.CompanyId = @TenantId
              AND ts.GcRecord = 0;
            """;

        var ticketStatus = await connection.QuerySingleOrDefaultAsync<TicketStatusDto>(
            new CommandDefinition(sql, new { request.Id, TenantId = currentUserService.CompanyId }, cancellationToken: cancellationToken));

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
