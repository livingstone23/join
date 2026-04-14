using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Messaging.Tickets.Queries;

/// <summary>
/// Handles paginated ticket queries using Dapper for high-performance reads.
/// </summary>
public sealed class GetTicketsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetTicketsQuery, Response<PagedResult<TicketListItemDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    /// <summary>
    /// Retrieves a tenant-scoped paginated ticket list with optional filters.
    /// </summary>
    public async Task<Response<PagedResult<TicketListItemDto>>> Handle(GetTicketsQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PagedResult<TicketListItemDto>>.Error(
                "COMPANY_REQUIRED",
                ["The X-Company-Id header is required."]);
        }

        var defaultPageNumber = _paginationSettings.DefaultPageNumber < 1 ? 1 : _paginationSettings.DefaultPageNumber;
        var defaultPageSize = _paginationSettings.DefaultPageSize < 1 ? 10 : _paginationSettings.DefaultPageSize;
        var maxPageSize = _paginationSettings.MaxPageSize < defaultPageSize ? defaultPageSize : _paginationSettings.MaxPageSize;

        var sanitizedPageNumber = request.PageNumber.GetValueOrDefault(defaultPageNumber);
        sanitizedPageNumber = sanitizedPageNumber < 1 ? defaultPageNumber : sanitizedPageNumber;

        var requestedPageSize = request.PageSize.GetValueOrDefault(defaultPageSize);
        var sanitizedPageSize = requestedPageSize < 1 ? defaultPageSize : Math.Min(requestedPageSize, maxPageSize);
        var offset = (sanitizedPageNumber - 1) * sanitizedPageSize;

        using var connection = connectionFactory.CreateConnection();

        var parameters = new DynamicParameters();
        parameters.Add("TenantId", currentUserService.CompanyId);
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", sanitizedPageSize);

        var whereBuilder = new StringBuilder("WHERE t.CompanyId = @TenantId AND t.GcRecord = 0");

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            whereBuilder.Append(" AND (t.Code LIKE @Search OR t.Name LIKE @Search OR t.Description LIKE @Search)");
            parameters.Add("Search", $"%{request.Search.Trim()}%");
        }

        if (request.TicketStatusId.HasValue && request.TicketStatusId.Value != Guid.Empty)
        {
            whereBuilder.Append(" AND t.TicketStatusId = @TicketStatusId");
            parameters.Add("TicketStatusId", request.TicketStatusId.Value);
        }

        if (request.TicketComplexityId.HasValue && request.TicketComplexityId.Value != Guid.Empty)
        {
            whereBuilder.Append(" AND t.TicketComplexityId = @TicketComplexityId");
            parameters.Add("TicketComplexityId", request.TicketComplexityId.Value);
        }

        if (request.AssignedToUserId.HasValue && request.AssignedToUserId.Value != Guid.Empty)
        {
            whereBuilder.Append(" AND t.AssignedToUserId = @AssignedToUserId");
            parameters.Add("AssignedToUserId", request.AssignedToUserId.Value);
        }

        if (request.CustomerId.HasValue && request.CustomerId.Value != Guid.Empty)
        {
            whereBuilder.Append(" AND t.CustomerId = @CustomerId");
            parameters.Add("CustomerId", request.CustomerId.Value);
        }

        if (request.ProjectId.HasValue && request.ProjectId.Value != Guid.Empty)
        {
            whereBuilder.Append(" AND t.ProjectId = @ProjectId");
            parameters.Add("ProjectId", request.ProjectId.Value);
        }

        if (request.IsVisibleToExternals.HasValue)
        {
            whereBuilder.Append(" AND t.IsVisibleToExternals = @IsVisibleToExternals");
            parameters.Add("IsVisibleToExternals", request.IsVisibleToExternals.Value);
        }

        if (request.FromDate.HasValue)
        {
            whereBuilder.Append(" AND t.Created >= @FromDate");
            parameters.Add("FromDate", request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            whereBuilder.Append(" AND t.Created <= @ToDate");
            parameters.Add("ToDate", request.ToDate.Value);
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                t.Id,
                t.Code,
                t.Name,
                t.TicketStatusId,
                ts.Name AS TicketStatusName,
                t.TicketComplexityId,
                tc.Name AS TicketComplexityName,
                t.CustomerId,
                CASE
                    WHEN c.Id IS NULL THEN NULL
                    WHEN c.CommercialName IS NOT NULL AND c.CommercialName <> '' THEN c.CommercialName
                    ELSE CONCAT(c.FirstName, ' ', COALESCE(c.MiddleName, ''), ' ', COALESCE(c.LastName, ''), ' ', COALESCE(c.SecondLastName, ''))
                END AS CustomerName,
                t.AssignedToUserId,
                CASE
                    WHEN au.Id IS NULL THEN NULL
                    ELSE CONCAT(au.FirstName, ' ', au.LastName)
                END AS AssignedToUserName,
                t.Created AS CreatedAt
            FROM Messaging.Tickets t
            INNER JOIN Messaging.TicketStatuses ts ON t.TicketStatusId = ts.Id
            INNER JOIN Messaging.TicketComplexities tc ON t.TicketComplexityId = tc.Id
            LEFT JOIN Admin.Customers c ON t.CustomerId = c.Id
            LEFT JOIN Security.Users au ON t.AssignedToUserId = au.Id
            {whereClause}
            ORDER BY t.Created DESC, t.Code DESC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Messaging.Tickets t
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<TicketListItemDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<TicketListItemDto>>
        {
            IsSuccess = true,
            Message = "Tickets retrieved successfully.",
            Data = new PagedResult<TicketListItemDto>
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
