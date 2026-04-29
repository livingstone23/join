using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;



namespace JOIN.Application.UseCases.Security.SystemOptions.Queries;



/// <summary>
/// Handler para paginación de SystemOptions globales, siguiendo el patrón de TimeUnit.
/// </summary>
public sealed class GetSystemOptionsPagedQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetSystemOptionsPagedQuery, Response<PagedResult<SystemOptionListItemDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<SystemOptionListItemDto>>> Handle(GetSystemOptionsPagedQuery request, CancellationToken cancellationToken)
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

        var parameters = new Dapper.DynamicParameters();
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", sanitizedPageSize);

        var whereBuilder = new StringBuilder("WHERE GcRecord = 0");
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            whereBuilder.Append(" AND Name LIKE @Name");
            parameters.Add("Name", $"%{request.Name.Trim()}%");
        }

        var whereClause = whereBuilder.ToString();

        var sql = $@"
            SELECT
                Id,
                ModuleId,
                Name,
                Route,
                ParentId,
                Created,
                ModuleName = ''
            FROM Security.SystemOptions
            {whereClause}
            ORDER BY Name ASC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Security.SystemOptions
            {whereClause};
        ";

        using var multi = await connection.QueryMultipleAsync(
            new Dapper.CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<SystemOptionListItemDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<SystemOptionListItemDto>>
        {
            IsSuccess = true,
            Message = "System options retrieved successfully.",
            Data = new PagedResult<SystemOptionListItemDto>
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
