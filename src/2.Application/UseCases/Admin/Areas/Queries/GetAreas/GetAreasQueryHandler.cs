using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Admin.Areas.Queries;

/// <summary>
/// Handles tenant-scoped area list queries using Dapper for high-performance reads.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
/// <param name="paginationOptions">Configurable pagination defaults for the area listing endpoint.</param>
public sealed class GetAreasQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetAreasQuery, Response<PagedResult<AreaListItemDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    /// <summary>
    /// Retrieves a paginated list of active areas that belong to the requested company.
    /// </summary>
    /// <param name="request">The tenant-scoped list query.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized paged response containing the matching areas.</returns>
    public async Task<Response<PagedResult<AreaListItemDto>>> Handle(GetAreasQuery request, CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Response<PagedResult<AreaListItemDto>>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]);
        }

        if (request.CreatedFrom.HasValue && request.CreatedTo.HasValue && request.CreatedFrom.Value.Date > request.CreatedTo.Value.Date)
        {
            return Response<PagedResult<AreaListItemDto>>.Error(
                "INVALID_CREATED_RANGE",
                ["CreatedFrom must be less than or equal to CreatedTo."]);
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
        parameters.Add("CompanyId", request.CompanyId);
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", sanitizedPageSize);

        var whereBuilder = new StringBuilder("WHERE a.CompanyId = @CompanyId AND a.GcRecord = 0");

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            whereBuilder.Append(" AND a.Name LIKE @Name");
            parameters.Add("Name", $"%{request.Name.Trim()}%");
        }

        if (request.CreatedFrom.HasValue)
        {
            whereBuilder.Append(" AND a.Created >= @CreatedFrom");
            parameters.Add("CreatedFrom", request.CreatedFrom.Value.Date);
        }

        if (request.CreatedTo.HasValue)
        {
            whereBuilder.Append(" AND a.Created < @CreatedToExclusive");
            parameters.Add("CreatedToExclusive", request.CreatedTo.Value.Date.AddDays(1));
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                a.Id,
                a.CompanyId,
                c.Name AS CompanyName,
                a.Name,
                a.EntityStatusId,
                es.Name AS EntityStatusName,
                a.Created
            FROM Admin.Areas a
            INNER JOIN Admin.EntityStatuses es
                ON es.Id = a.EntityStatusId
               AND es.GcRecord = 0
            INNER JOIN Common.Companies c
                ON c.Id = a.CompanyId
               AND c.GcRecord = 0
            {whereClause}
            ORDER BY a.Created DESC, a.Name ASC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Admin.Areas a
            INNER JOIN Common.Companies c
                ON c.Id = a.CompanyId
               AND c.GcRecord = 0
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<AreaListItemDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<AreaListItemDto>>
        {
            IsSuccess = true,
            Message = "Areas retrieved successfully.",
            Data = new PagedResult<AreaListItemDto>
            {
                Items = items,
                PageNumber = sanitizedPageNumber,
                PageSize = sanitizedPageSize,
                TotalCount = totalCount,
                TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)sanitizedPageSize)
            }
        };
    }

    /// <summary>
    /// Resolves a provider-compatible pagination clause.
    /// </summary>
    /// <param name="connection">The active database connection.</param>
    /// <returns>The pagination SQL fragment for the current provider.</returns>
    private static string GetPaginationClause(IDbConnection connection)
        => connection.GetType().Name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
            ? "LIMIT @PageSize OFFSET @Offset"
            : "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
}
