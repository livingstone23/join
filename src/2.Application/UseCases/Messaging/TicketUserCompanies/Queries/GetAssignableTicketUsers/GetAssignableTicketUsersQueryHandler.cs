using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetAssignableTicketUsers;

/// <summary>
/// Returns the active resolvers of the current tenant's ticket roster
/// (SPEC 53). Dapper over <c>Messaging.TicketUserCompanies</c> joined to
/// <c>Security.Users</c>; same <c>UserName</c> projection as
/// <see cref="GetTicketUserCompaniesQueryHandler"/>. Besides the roster row, the user
/// must be active, not deleted and still linked to the company (<c>UserCompanies</c>):
/// <c>CreateTicket</c>/<c>ReassignTicket</c> reject a user without that link
/// (<c>INVALID_ASSIGNED_USER_TENANT</c>), so it is never offered. Unpaged because a roster
/// per tenant is small; if it grows past a reasonable limit, paginate in a
/// separate spec.
/// </summary>
public sealed class GetAssignableTicketUsersQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetAssignableTicketUsersQuery, Response<IReadOnlyList<TicketAssignableUserDto>>>
{
    private const string Sql = """
        SELECT
            tuc.UserId AS UserId,
            CONCAT(u.FirstName, ' ', u.LastName) AS UserName,
            u.Email AS UserEmail
        FROM Messaging.TicketUserCompanies tuc
        INNER JOIN Security.Users u ON u.Id = tuc.UserId
        WHERE tuc.CompanyId = @TenantId
          AND tuc.GcRecord = 0
          AND tuc.CanResolveTicket = TRUE
          AND u.GcRecord = 0
          AND u.IsActive = TRUE
          AND EXISTS (
              SELECT 1
              FROM Security.UserCompanies uc
              WHERE uc.UserId = tuc.UserId
                AND uc.CompanyId = tuc.CompanyId
                AND uc.GcRecord = 0)
        ORDER BY u.LastName ASC, u.FirstName ASC;
        """;

    public async Task<Response<IReadOnlyList<TicketAssignableUserDto>>> Handle(
        GetAssignableTicketUsersQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IReadOnlyList<TicketAssignableUserDto>>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        // SPEC 53: always the active company of the token, SuperAdmin included.
        var tenantId = currentUserService.CompanyId;

        using var connection = connectionFactory.CreateConnection();
        var rows = (await connection.QueryAsync<TicketAssignableUserDto>(
            new CommandDefinition(Sql, new { TenantId = tenantId }, cancellationToken: cancellationToken))).AsList();

        return new Response<IReadOnlyList<TicketAssignableUserDto>>
        {
            IsSuccess = true,
            Message = "Assignable ticket users retrieved successfully.",
            Data = rows
        };
    }
}