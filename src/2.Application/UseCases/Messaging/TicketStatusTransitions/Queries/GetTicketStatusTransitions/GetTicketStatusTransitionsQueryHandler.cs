using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketStatusTransitions.Queries.GetTicketStatusTransitions;

/// <summary>
/// Handles <see cref="GetTicketStatusTransitionsQuery"/>: a flat, tenant-scoped read of all
/// active transition rules, with an optional <c>FromStatusId</c> filter. Joins
/// <c>TicketStatuses</c> twice for the display names of the endpoints.
/// </summary>
public sealed class GetTicketStatusTransitionsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetTicketStatusTransitionsQuery, Response<IEnumerable<TicketStatusTransitionDto>>>
{
    private readonly ICurrentUserService _currentUserService = currentUserService;

    public async Task<Response<IEnumerable<TicketStatusTransitionDto>>> Handle(
        GetTicketStatusTransitionsQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IEnumerable<TicketStatusTransitionDto>>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var parameters = new DynamicParameters();
        parameters.Add("TenantId", TenantResolver.Resolve(_currentUserService, request.CompanyId));

        // SPEC 41: deleted rules only for a SuperAdmin asking includeDeleted=true.
        var whereBuilder = new StringBuilder(SoftDeleteVisibility.IncludeDeleted(_currentUserService, request.IncludeDeleted)
            ? "WHERE tst.CompanyId = @TenantId"
            : "WHERE tst.CompanyId = @TenantId AND tst.GcRecord = 0");

        if (request.FromStatusId.HasValue && request.FromStatusId.Value != Guid.Empty)
        {
            whereBuilder.Append(" AND tst.FromStatusId = @FromStatusId");
            parameters.Add("FromStatusId", request.FromStatusId.Value);
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                tst.Id,
                tst.GcRecord,
                tst.FromStatusId,
                fs.Name AS FromStatusName,
                tst.ToStatusId,
                ts.Name  AS ToStatusName,
                tst.Created AS CreatedAt
            FROM Messaging.TicketStatusTransitions tst
            INNER JOIN Messaging.TicketStatuses fs ON tst.FromStatusId = fs.Id AND fs.GcRecord = 0
            INNER JOIN Messaging.TicketStatuses ts  ON tst.ToStatusId   = ts.Id  AND ts.GcRecord = 0
            {whereClause}
            ORDER BY fs.Name ASC, ts.Name ASC;
            """;

        using var connection = connectionFactory.CreateConnection();

        var items = (await connection.QueryAsync<TicketStatusTransitionDto>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))).AsList();

        return new Response<IEnumerable<TicketStatusTransitionDto>>
        {
            IsSuccess = true,
            Message = "Ticket status transitions retrieved successfully.",
            Data = items
        };
    }
}