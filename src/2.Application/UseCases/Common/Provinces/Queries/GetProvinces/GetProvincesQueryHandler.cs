using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Common.Provinces.Queries;

/// <summary>
/// Handles paginated province queries using Dapper for high-performance reads.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
/// <param name="paginationOptions">Configurable pagination defaults for the province listing endpoint.</param>
public sealed class GetProvincesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetProvincesQuery, Response<PagedResult<ProvinceDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    /// <summary>
    /// Retrieves a paginated list of provinces with optional filters by name, code, and country.
    /// </summary>
    /// <param name="request">The pagination and filtering request payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized paged response containing the matching provinces.</returns>
    public async Task<Response<PagedResult<ProvinceDto>>> Handle(GetProvincesQuery request, CancellationToken cancellationToken)
    {
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
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", sanitizedPageSize);

        var whereBuilder = new StringBuilder("WHERE p.GcRecord = 0");

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            whereBuilder.Append(" AND p.Name LIKE @Name");
            parameters.Add("Name", $"%{request.Name.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(request.Code))
        {
            whereBuilder.Append(" AND p.Code = @Code");
            parameters.Add("Code", request.Code.Trim().ToUpperInvariant());
        }

        if (request.CountryId.HasValue && request.CountryId.Value != Guid.Empty)
        {
            whereBuilder.Append(" AND p.CountryId = @CountryId");
            parameters.Add("CountryId", request.CountryId.Value);
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                p.Id,
                p.Name,
                p.Code,
                p.CountryId,
                c.Name AS CountryName,
                p.RegionId,
                r.Name AS RegionName,
                p.Created AS CreatedAt
            FROM Common.Provinces p
            INNER JOIN Common.Countries c
                ON c.Id = p.CountryId
               AND c.GcRecord = 0
            LEFT JOIN Common.Regions r
                ON r.Id = p.RegionId
               AND r.GcRecord = 0
            {whereClause}
            ORDER BY p.Created DESC, p.Name ASC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Common.Provinces p
            INNER JOIN Common.Countries c
                ON c.Id = p.CountryId
               AND c.GcRecord = 0
            LEFT JOIN Common.Regions r
                ON r.Id = p.RegionId
               AND r.GcRecord = 0
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<ProvinceDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<ProvinceDto>>
        {
            IsSuccess = true,
            Message = "Provinces retrieved successfully.",
            Data = new PagedResult<ProvinceDto>
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