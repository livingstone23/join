using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Admin.IdentificationTypes.Queries;

/// <summary>
/// Handles paginated identification type queries using Dapper for high-performance reads.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
/// <param name="paginationOptions">Configurable pagination defaults for the identification type listing endpoint.</param>
public sealed class GetIdentificationTypesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetIdentificationTypesQuery, Response<PagedResult<IdentificationTypeListItemDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    /// <summary>
    /// Retrieves a paginated list of identification document types.
    /// </summary>
    /// <param name="request">The incoming paginated query.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized paged response containing the matching identification types.</returns>
    public async Task<Response<PagedResult<IdentificationTypeListItemDto>>> Handle(GetIdentificationTypesQuery request, CancellationToken cancellationToken)
    {
        if (request.CreatedFrom.HasValue && request.CreatedTo.HasValue && request.CreatedFrom.Value.Date > request.CreatedTo.Value.Date)
        {
            return Response<PagedResult<IdentificationTypeListItemDto>>.Error(
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
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", sanitizedPageSize);

        var whereBuilder = new StringBuilder("WHERE it.GcRecord = 0");

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            whereBuilder.Append(" AND it.Name LIKE @Name");
            parameters.Add("Name", $"%{request.Name.Trim()}%");
        }

        if (request.Created.HasValue)
        {
            whereBuilder.Append(" AND it.Created >= @CreatedDayStart AND it.Created < @CreatedDayEnd");
            parameters.Add("CreatedDayStart", request.Created.Value.Date);
            parameters.Add("CreatedDayEnd", request.Created.Value.Date.AddDays(1));
        }

        if (request.CreatedFrom.HasValue)
        {
            whereBuilder.Append(" AND it.Created >= @CreatedFrom");
            parameters.Add("CreatedFrom", request.CreatedFrom.Value.Date);
        }

        if (request.CreatedTo.HasValue)
        {
            whereBuilder.Append(" AND it.Created < @CreatedToExclusive");
            parameters.Add("CreatedToExclusive", request.CreatedTo.Value.Date.AddDays(1));
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                it.Id,
                it.Name,
                it.IsActive,
                it.Created AS CreatedAt
            FROM Admin.IdentificationTypes it
            {whereClause}
            ORDER BY it.Name ASC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Admin.IdentificationTypes it
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<IdentificationTypeListItemDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<IdentificationTypeListItemDto>>
        {
            IsSuccess = true,
            Message = "Identification types retrieved successfully.",
            Data = new PagedResult<IdentificationTypeListItemDto>
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