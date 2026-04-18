using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Messaging.TicketStatuses.Queries;

/// <summary>
/// Handles high-performance system-wide ticket status queries for SuperAdmin users.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
/// <param name="paginationOptions">Configurable pagination defaults for the ticket status listing endpoint.</param>
public sealed class GetSystemWideTicketStatusesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetSystemWideTicketStatusesQuery, Response<PagedResult<TicketStatusDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    /// <summary>
    /// Retrieves a paginated list of ticket statuses across all companies with optional filters.
    /// </summary>
    public async Task<Response<PagedResult<TicketStatusDto>>> Handle(GetSystemWideTicketStatusesQuery request, CancellationToken cancellationToken)
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

        var whereBuilder = new StringBuilder("WHERE ts.GcRecord = 0 AND c.GcRecord = 0");

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            whereBuilder.Append(" AND ts.Name LIKE @Name");
            parameters.Add("Name", $"%{request.Name.Trim()}%");
        }

        if (request.IsActive.HasValue)
        {
            whereBuilder.Append(" AND ts.IsActive = @IsActive");
            parameters.Add("IsActive", request.IsActive.Value);
        }

        if (request.IsInitial.HasValue)
        {
            whereBuilder.Append(" AND ts.IsInitial = @IsInitial");
            parameters.Add("IsInitial", request.IsInitial.Value);
        }

        if (request.IsPaused.HasValue)
        {
            whereBuilder.Append(" AND ts.IsPaused = @IsPaused");
            parameters.Add("IsPaused", request.IsPaused.Value);
        }

        if (request.IsFinal.HasValue)
        {
            whereBuilder.Append(" AND ts.IsFinal = @IsFinal");
            parameters.Add("IsFinal", request.IsFinal.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.CompanyName))
        {
            whereBuilder.Append(" AND c.Name LIKE @CompanyName");
            parameters.Add("CompanyName", $"%{request.CompanyName.Trim()}%");
        }

        if (request.Code.HasValue)
        {
            whereBuilder.Append(" AND ts.Code = @Code");
            parameters.Add("Code", request.Code.Value);
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                ts.Id,
                ts.CompanyId,
                c.Name AS CompanyName,
                ts.Name,
                ts.Description,
                ts.Code,
                ts.IsActive,
                ts.IsInitial,
                ts.IsPaused,
                ts.IsFinal,
                ts.Created AS CreatedAt
            FROM Messaging.TicketStatuses ts
            INNER JOIN Common.Companies c ON c.Id = ts.CompanyId
            {whereClause}
            ORDER BY c.Name ASC, ts.Created DESC, ts.Name ASC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Messaging.TicketStatuses ts
            INNER JOIN Common.Companies c ON c.Id = ts.CompanyId
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<TicketStatusDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<TicketStatusDto>>
        {
            IsSuccess = true,
            Message = "System-wide ticket statuses retrieved successfully.",
            Data = new PagedResult<TicketStatusDto>
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
