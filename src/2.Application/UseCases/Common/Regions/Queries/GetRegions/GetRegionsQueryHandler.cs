using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Common.Regions.Queries;

/// <summary>
/// Handles paginated region queries using Dapper for high-performance reads.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
/// <param name="paginationOptions">Configurable pagination defaults for the region listing endpoint.</param>
public sealed class GetRegionsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetRegionsQuery, Response<PagedResult<RegionDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    /// <summary>
    /// Retrieves a paginated list of regions with optional filters by name, code, and country.
    /// </summary>
    /// <param name="request">The pagination and filtering request payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized paged response containing the matching regions.</returns>
    public async Task<Response<PagedResult<RegionDto>>> Handle(GetRegionsQuery request, CancellationToken cancellationToken)
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

        var whereBuilder = new StringBuilder("WHERE r.GcRecord = 0");

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            whereBuilder.Append(" AND r.Name LIKE @Name");
            parameters.Add("Name", $"%{request.Name.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(request.Code))
        {
            whereBuilder.Append(" AND r.Code = @Code");
            parameters.Add("Code", request.Code.Trim().ToUpperInvariant());
        }

        if (request.CountryId.HasValue && request.CountryId.Value != Guid.Empty)
        {
            whereBuilder.Append(" AND r.CountryId = @CountryId");
            parameters.Add("CountryId", request.CountryId.Value);
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                r.Id,
                r.Name,
                r.Code,
                r.CountryId,
                c.Name AS CountryName,
                r.Created AS CreatedAt
            FROM Common.Regions r
            INNER JOIN Common.Countries c
                ON c.Id = r.CountryId
               AND c.GcRecord = 0
            {whereClause}
            ORDER BY r.Created DESC, r.Name ASC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Common.Regions r
            INNER JOIN Common.Countries c
                ON c.Id = r.CountryId
               AND c.GcRecord = 0
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<RegionDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<RegionDto>>
        {
            IsSuccess = true,
            Message = "Regions retrieved successfully.",
            Data = new PagedResult<RegionDto>
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
