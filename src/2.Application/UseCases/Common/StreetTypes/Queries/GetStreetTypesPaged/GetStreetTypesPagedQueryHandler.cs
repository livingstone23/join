using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Common.StreetTypes.Queries;

/// <summary>
/// Handles paginated street type queries using Dapper.
/// </summary>
/// <param name="connectionFactory">Factory used to create DB-agnostic read connections.</param>
public class GetStreetTypesPagedQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetStreetTypesPagedQuery, Response<PagedResult<StreetTypeListItemDto>>>
{
    private const int MaxPageSize = 50;
    private const int DefaultPageSize = 10;

    /// <summary>
    /// Retrieves a paginated list of active street types.
    /// </summary>
    public async Task<Response<PagedResult<StreetTypeListItemDto>>> Handle(GetStreetTypesPagedQuery request, CancellationToken cancellationToken)
    {
        var sanitizedPageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
        var sanitizedPageSize = request.PageSize < 1 ? DefaultPageSize : Math.Min(request.PageSize, MaxPageSize);
        var offset = (sanitizedPageNumber - 1) * sanitizedPageSize;

        using var connection = connectionFactory.CreateConnection();

        var parameters = new DynamicParameters();
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", sanitizedPageSize);

        var whereBuilder = new StringBuilder("WHERE st.GcRecord = 0");

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            whereBuilder.Append(" AND (st.Name LIKE @SearchTerm OR st.Abbreviation LIKE @SearchTerm)");
            parameters.Add("SearchTerm", $"%{request.SearchTerm.Trim()}%");
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                st.Id,
                st.Name,
                st.Abbreviation,
                st.IsActive
            FROM Common.StreetTypes st
            {whereClause}
            ORDER BY st.Created DESC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Common.StreetTypes st
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<StreetTypeListItemDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<StreetTypeListItemDto>>
        {
            IsSuccess = true,
            Message = "Street types retrieved successfully.",
            Data = new PagedResult<StreetTypeListItemDto>
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
    private static string GetPaginationClause(IDbConnection connection)
        => connection.GetType().Name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
            ? "LIMIT @PageSize OFFSET @Offset"
            : "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
}
