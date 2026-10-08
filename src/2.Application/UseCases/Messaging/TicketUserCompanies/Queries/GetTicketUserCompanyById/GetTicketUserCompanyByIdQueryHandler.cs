using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetTicketUserCompanyById;

/// <summary>
/// Handles <see cref="GetTicketUserCompanyByIdQuery"/> via Dapper for read performance,
/// enforcing the tenant filter and the soft-delete flag at the SQL level.
/// </summary>
public sealed class GetTicketUserCompanyByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetTicketUserCompanyByIdQuery, Response<TicketUserCompanyDto>>
{
    public async Task<Response<TicketUserCompanyDto>> Handle(
        GetTicketUserCompanyByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<TicketUserCompanyDto>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        // SPEC 41: deleted rows only for a SuperAdmin asking includeDeleted=true.
        var activeOnly = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted)
            ? string.Empty
            : "AND tuc.GcRecord = 0";

        var sql = $"""
            SELECT
                tuc.Id,
                tuc.GcRecord,
                tuc.CompanyId,
                co.Name AS CompanyName,
                tuc.UserId,
                CONCAT(u.FirstName, ' ', u.LastName) AS UserName,
                u.Email AS UserEmail,
                tuc.IsSuperAdminTicket,
                tuc.CanFinishTicket,
                tuc.CanResolveTicket,
                tuc.Created AS CreatedAt
            FROM Messaging.TicketUserCompanies tuc
            INNER JOIN Security.Users u ON tuc.UserId = u.Id
            LEFT JOIN Common.Companies co ON tuc.CompanyId = co.Id
            WHERE tuc.Id = @Id
              AND tuc.CompanyId = @TenantId
              {activeOnly};
            """;

        using var connection = connectionFactory.CreateConnection();

        var row = await connection.QueryFirstOrDefaultAsync<TicketUserCompanyDto>(
            new CommandDefinition(
                sql,
                new { request.Id, TenantId = TenantResolver.Resolve(currentUserService, request.CompanyId) },
                cancellationToken: cancellationToken));

        if (row is null)
        {
            return Response<TicketUserCompanyDto>.Error(
                "TICKET_USER_COMPANY_NOT_FOUND",
                ["Ticket roster entry not found for the current company."]);
        }

        return new Response<TicketUserCompanyDto>
        {
            IsSuccess = true,
            Message = "Ticket roster entry retrieved successfully.",
            Data = row
        };
    }
}
