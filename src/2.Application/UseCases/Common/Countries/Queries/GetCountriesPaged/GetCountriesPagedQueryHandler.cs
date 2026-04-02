using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Common.Countries.Queries;

/// <summary>
/// Handles paginated country queries using Dapper for high-performance reads.
/// </summary>
/// <param name="connectionFactory">Factory used to create DB-agnostic read connections.</param>
public class GetCountriesPagedQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetCountriesPagedQuery, Response<PagedResult<CountryListItemDto>>>
{
    private const int MaxPageSize = 50;
    private const int DefaultPageSize = 10;

    /// <summary>
    /// Retrieves a paginated list of active countries.
    /// </summary>
    /// <param name="request">The pagination and search request payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized paginated response.</returns>
    public async Task<Response<PagedResult<CountryListItemDto>>> Handle(GetCountriesPagedQuery request, CancellationToken cancellationToken)
    {
        var sanitizedPageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
        var sanitizedPageSize = request.PageSize < 1 ? DefaultPageSize : Math.Min(request.PageSize, MaxPageSize);
        var offset = (sanitizedPageNumber - 1) * sanitizedPageSize;

        using var connection = connectionFactory.CreateConnection();

        var parameters = new DynamicParameters();
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", sanitizedPageSize);

        var whereBuilder = new StringBuilder("WHERE c.GcRecord = 0");

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            whereBuilder.Append(" AND c.Name LIKE @SearchTerm");
            parameters.Add("SearchTerm", $"%{request.SearchTerm.Trim()}%");
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                c.Id,
                c.Name,
                c.IsoCode
            FROM Common.Countries c
            {whereClause}
            ORDER BY c.Created DESC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Common.Countries c
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<CountryListItemDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<CountryListItemDto>>
        {
            IsSuccess = true,
            Message = "Countries retrieved successfully.",
            Data = new PagedResult<CountryListItemDto>
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
