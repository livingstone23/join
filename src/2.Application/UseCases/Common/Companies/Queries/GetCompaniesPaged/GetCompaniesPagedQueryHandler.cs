using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Common.Companies.Queries;

/// <summary>
/// Handles paginated company queries using Dapper.
/// </summary>
/// <param name="connectionFactory">Factory used to create DB-agnostic read connections.</param>
public class GetCompaniesPagedQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetCompaniesPagedQuery, Response<PagedResult<CompanyListItemDto>>>
{
    private const int MaxPageSize = 50;
    private const int DefaultPageSize = 10;

    /// <summary>
    /// Retrieves a paginated list of active companies.
    /// </summary>
    public async Task<Response<PagedResult<CompanyListItemDto>>> Handle(GetCompaniesPagedQuery request, CancellationToken cancellationToken)
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
            whereBuilder.Append(" AND (c.Name LIKE @SearchTerm OR c.TaxId LIKE @SearchTerm)");
            parameters.Add("SearchTerm", $"%{request.SearchTerm.Trim()}%");
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                c.Id,
                c.Name,
                c.TaxId,
                c.IsActive
            FROM Common.Companies c
            {whereClause}
            ORDER BY c.Created DESC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Common.Companies c
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<CompanyListItemDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<CompanyListItemDto>>
        {
            IsSuccess = true,
            Message = "Companies retrieved successfully.",
            Data = new PagedResult<CompanyListItemDto>
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
