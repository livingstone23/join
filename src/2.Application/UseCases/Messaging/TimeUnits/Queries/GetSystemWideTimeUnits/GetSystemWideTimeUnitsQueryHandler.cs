using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Messaging.TimeUnits.Queries;

/// <summary>
/// Handles high-performance system-wide time unit queries for SuperAdmin users.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
/// <param name="paginationOptions">Configurable pagination defaults for the time unit listing endpoint.</param>
public sealed class GetSystemWideTimeUnitsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetSystemWideTimeUnitsQuery, Response<PagedResult<TimeUnitDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    /// <summary>
    /// Retrieves a paginated list of time units across all companies with optional filters.
    /// Includes logically deleted rows.
    /// </summary>
    public async Task<Response<PagedResult<TimeUnitDto>>> Handle(GetSystemWideTimeUnitsQuery request, CancellationToken cancellationToken)
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

        var whereBuilder = new StringBuilder("WHERE 1 = 1");

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            whereBuilder.Append(" AND tu.Name LIKE @Name");
            parameters.Add("Name", $"%{request.Name.Trim()}%");
        }

        if (request.IsActive.HasValue)
        {
            whereBuilder.Append(" AND tu.IsActive = @IsActive");
            parameters.Add("IsActive", request.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.CompanyName))
        {
            whereBuilder.Append(" AND c.Name LIKE @CompanyName");
            parameters.Add("CompanyName", $"%{request.CompanyName.Trim()}%");
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                tu.Id,
                tu.CompanyId,
                c.Name AS CompanyName,
                tu.Name,
                tu.Code,
                tu.IsActive,
                tu.Created AS CreatedAt
            FROM Messaging.TimeUnits tu
            LEFT JOIN Common.Companies c ON c.Id = tu.CompanyId
            {whereClause}
            ORDER BY c.Name ASC, tu.Created DESC, tu.Name ASC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Messaging.TimeUnits tu
            LEFT JOIN Common.Companies c ON c.Id = tu.CompanyId
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<TimeUnitDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<TimeUnitDto>>
        {
            IsSuccess = true,
            Message = "System-wide time units retrieved successfully.",
            Data = new PagedResult<TimeUnitDto>
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
