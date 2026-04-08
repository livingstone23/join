using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Admin.EntityStatuses.Queries;

/// <summary>
/// Handles paginated entity status queries using Dapper for high-performance reads.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
/// <param name="paginationOptions">Configurable pagination defaults for the entity status listing endpoint.</param>
public sealed class GetEntityStatusQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetEntityStatusQuery, Response<PagedResult<EntityStatusListItemDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    /// <summary>
    /// Retrieves a paginated list of active entity statuses.
    /// </summary>
    /// <param name="request">The incoming paginated query.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized paged response containing the matching entity statuses.</returns>
    public async Task<Response<PagedResult<EntityStatusListItemDto>>> Handle(GetEntityStatusQuery request, CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Response<PagedResult<EntityStatusListItemDto>>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]);
        }

        if (request.CreatedFrom.HasValue && request.CreatedTo.HasValue && request.CreatedFrom.Value.Date > request.CreatedTo.Value.Date)
        {
            return Response<PagedResult<EntityStatusListItemDto>>.Error(
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

        const string companyValidationSql = """
            SELECT COUNT(1)
            FROM Common.Companies c
            WHERE c.Id = @CompanyId
              AND c.GcRecord = 0;
            """;

        var companyExists = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(companyValidationSql, new { request.CompanyId }, cancellationToken: cancellationToken));

        if (companyExists == 0)
        {
            return Response<PagedResult<EntityStatusListItemDto>>.Error(
                "INVALID_COMPANY_ID",
                ["The specified CompanyId does not exist."]);
        }

        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", request.CompanyId);
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", sanitizedPageSize);

        var whereBuilder = new StringBuilder("WHERE es.GcRecord = 0");

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            whereBuilder.Append(" AND es.Name LIKE @Name");
            parameters.Add("Name", $"%{request.Name.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(request.ModuleName))
        {
            whereBuilder.Append(" AND COALESCE(es.Description, '') LIKE @ModuleName");
            parameters.Add("ModuleName", $"%{request.ModuleName.Trim()}%");
        }

        if (request.CreatedFrom.HasValue)
        {
            whereBuilder.Append(" AND es.Created >= @CreatedFrom");
            parameters.Add("CreatedFrom", request.CreatedFrom.Value.Date);
        }

        if (request.CreatedTo.HasValue)
        {
            whereBuilder.Append(" AND es.Created < @CreatedToExclusive");
            parameters.Add("CreatedToExclusive", request.CreatedTo.Value.Date.AddDays(1));
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                es.Id,
                es.Name,
                es.Description,
                es.Code,
                es.IsOperative,
                es.Created AS CreatedAt
            FROM Admin.EntityStatuses es
            {whereClause}
            ORDER BY es.Code ASC, es.Name ASC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Admin.EntityStatuses es
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<EntityStatusListItemDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<EntityStatusListItemDto>>
        {
            IsSuccess = true,
            Message = "Entity statuses retrieved successfully.",
            Data = new PagedResult<EntityStatusListItemDto>
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
