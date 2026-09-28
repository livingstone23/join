using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetSystemWideTicketUserCompanies;

/// <summary>
/// Handles <see cref="GetSystemWideTicketUserCompaniesQuery"/>: same projection as the
/// tenant-scoped variant but without a tenant filter; the optional <c>CompanyName</c>
/// is matched with LIKE for cross-tenant browsing.
/// </summary>
public sealed class GetSystemWideTicketUserCompaniesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetSystemWideTicketUserCompaniesQuery, Response<PagedResult<TicketUserCompanyDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<TicketUserCompanyDto>>> Handle(
        GetSystemWideTicketUserCompaniesQuery request,
        CancellationToken cancellationToken)
    {
        var (sanitizedPageNumber, sanitizedPageSize) = _paginationSettings.Sanitize(request.PageNumber, request.PageSize);
        var offset = (sanitizedPageNumber - 1) * sanitizedPageSize;

        using var connection = connectionFactory.CreateConnection();

        var parameters = new DynamicParameters();
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", sanitizedPageSize);

        var whereBuilder = new StringBuilder("WHERE tuc.GcRecord = 0");

        if (request.UserId.HasValue && request.UserId.Value != Guid.Empty)
        {
            whereBuilder.Append(" AND tuc.UserId = @UserId");
            parameters.Add("UserId", request.UserId.Value);
        }

        if (request.IsSuperAdminTicket.HasValue)
        {
            whereBuilder.Append(" AND tuc.IsSuperAdminTicket = @IsSuperAdminTicket");
            parameters.Add("IsSuperAdminTicket", request.IsSuperAdminTicket.Value);
        }

        if (request.CanFinishTicket.HasValue)
        {
            whereBuilder.Append(" AND tuc.CanFinishTicket = @CanFinishTicket");
            parameters.Add("CanFinishTicket", request.CanFinishTicket.Value);
        }

        if (request.CanResolveTicket.HasValue)
        {
            whereBuilder.Append(" AND tuc.CanResolveTicket = @CanResolveTicket");
            parameters.Add("CanResolveTicket", request.CanResolveTicket.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.CompanyName))
        {
            whereBuilder.Append(" AND co.Name LIKE @CompanyName");
            parameters.Add("CompanyName", $"%{request.CompanyName.Trim()}%");
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                tuc.Id,
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
            {whereClause}
            ORDER BY tuc.Created DESC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Messaging.TicketUserCompanies tuc
            LEFT JOIN Common.Companies co ON tuc.CompanyId = co.Id
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<TicketUserCompanyDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<TicketUserCompanyDto>>
        {
            IsSuccess = true,
            Message = "Ticket roster entries retrieved successfully.",
            Data = new PagedResult<TicketUserCompanyDto>
            {
                Items = items,
                PageNumber = sanitizedPageNumber,
                PageSize = sanitizedPageSize,
                TotalCount = totalCount,
                TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)sanitizedPageSize)
            }
        };
    }

    private static string GetPaginationClause(IDbConnection connection)
        => connection.GetType().Name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
            ? "LIMIT @PageSize OFFSET @Offset"
            : "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
}
