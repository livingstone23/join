using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Common.CommunicationChannels.Queries;

/// <summary>
/// Handles paginated communication channel queries using Dapper.
/// </summary>
/// <param name="connectionFactory">Factory used to create DB-agnostic read connections.</param>
/// <param name="paginationOptions">Configurable pagination defaults shared across paged endpoints.</param>
public class GetCommunicationChannelsPagedQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetCommunicationChannelsPagedQuery, Response<PagedResult<CommunicationChannelListItemDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    /// <summary>
    /// Retrieves a paginated list of active communication channels.
    /// </summary>
    public async Task<Response<PagedResult<CommunicationChannelListItemDto>>> Handle(GetCommunicationChannelsPagedQuery request, CancellationToken cancellationToken)
    {
        var (sanitizedPageNumber, sanitizedPageSize) = _paginationSettings.Sanitize(request.PageNumber, request.PageSize);
        var offset = (sanitizedPageNumber - 1) * sanitizedPageSize;

        using var connection = connectionFactory.CreateConnection();

        var parameters = new DynamicParameters();
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", sanitizedPageSize);

        var whereBuilder = new StringBuilder("WHERE cc.GcRecord = 0");

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            whereBuilder.Append(" AND (cc.Name LIKE @SearchTerm OR cc.Provider LIKE @SearchTerm OR cc.Code LIKE @SearchTerm)");
            parameters.Add("SearchTerm", $"%{request.SearchTerm.Trim()}%");
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                cc.Id,
                cc.Name,
                cc.Provider,
                cc.Code,
                cc.IsActive
            FROM Common.CommunicationChannels cc
            {whereClause}
            ORDER BY cc.Created DESC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Common.CommunicationChannels cc
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<CommunicationChannelListItemDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<CommunicationChannelListItemDto>>
        {
            IsSuccess = true,
            Message = "Communication channels retrieved successfully.",
            Data = new PagedResult<CommunicationChannelListItemDto>
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
