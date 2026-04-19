using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Queries;

/// <summary>
/// Handles high-performance system-wide ticket default configuration queries for SuperAdmin users.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
/// <param name="paginationOptions">Configurable pagination defaults for the listing endpoint.</param>
public sealed class GetSystemWideTicketCompanyDefaultsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetSystemWideTicketCompanyDefaultsQuery, Response<PagedResult<TicketCompanyDefaultDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    /// <summary>
    /// Retrieves a paginated list of ticket default configurations across all companies with optional filters.
    /// </summary>
    public async Task<Response<PagedResult<TicketCompanyDefaultDto>>> Handle(GetSystemWideTicketCompanyDefaultsQuery request, CancellationToken cancellationToken)
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

        if (!string.IsNullOrWhiteSpace(request.CompanyName))
        {
            whereBuilder.Append(" AND c.Name LIKE @CompanyName");
            parameters.Add("CompanyName", $"%{request.CompanyName.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(request.StartCode))
        {
            whereBuilder.Append(" AND tcd.StartCode LIKE @StartCode");
            parameters.Add("StartCode", $"%{request.StartCode.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(request.TicketStatusDefaultName))
        {
            whereBuilder.Append(" AND ts.Name LIKE @TicketStatusDefaultName");
            parameters.Add("TicketStatusDefaultName", $"%{request.TicketStatusDefaultName.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(request.TicketComplexityDefaultName))
        {
            whereBuilder.Append(" AND tc.Name LIKE @TicketComplexityDefaultName");
            parameters.Add("TicketComplexityDefaultName", $"%{request.TicketComplexityDefaultName.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(request.TimeUnitDefaultName))
        {
            whereBuilder.Append(" AND tu.Name LIKE @TimeUnitDefaultName");
            parameters.Add("TimeUnitDefaultName", $"%{request.TimeUnitDefaultName.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(request.AreaName))
        {
            whereBuilder.Append(" AND a.Name LIKE @AreaName");
            parameters.Add("AreaName", $"%{request.AreaName.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(request.ProjectName))
        {
            whereBuilder.Append(" AND p.Name LIKE @ProjectName");
            parameters.Add("ProjectName", $"%{request.ProjectName.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(request.ChannelName))
        {
            whereBuilder.Append(" AND ch.Name LIKE @ChannelName");
            parameters.Add("ChannelName", $"%{request.ChannelName.Trim()}%");
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                tcd.Id,
                tcd.CompanyId,
                c.Name AS CompanyName,
                tcd.GcRecord,
                tcd.StartCode,
                tcd.CodeSequenceLength,
                tcd.UsePersonalizedCode,
                tcd.TicketStatusDefaultId,
                ts.Name AS StatusName,
                tcd.TicketComplexityDefaultId,
                tc.Name AS ComplexityName,
                tcd.TimeUnitDefaultId,
                tu.Name AS TimeUnitName,
                tcd.AreaDefaultId,
                a.Name AS AreaName,
                tcd.ProjectDefaultId,
                p.Name AS ProjectName,
                tcd.ChannelDefaultId,
                ch.Name AS ChannelName,
                tcd.Created AS CreatedAt
            FROM Messaging.TicketCompanyDefaults tcd
            LEFT JOIN Common.Companies c ON c.Id = tcd.CompanyId
            LEFT JOIN Messaging.TicketStatuses ts ON tcd.TicketStatusDefaultId = ts.Id
            LEFT JOIN Messaging.TicketComplexities tc ON tcd.TicketComplexityDefaultId = tc.Id
            LEFT JOIN Messaging.TimeUnits tu ON tcd.TimeUnitDefaultId = tu.Id
            LEFT JOIN Admin.Areas a ON tcd.AreaDefaultId = a.Id
            LEFT JOIN Admin.Projects p ON tcd.ProjectDefaultId = p.Id
            LEFT JOIN Common.CommunicationChannels ch ON tcd.ChannelDefaultId = ch.Id
            {whereClause}
            ORDER BY c.Name ASC, tcd.Created DESC, tcd.StartCode ASC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Messaging.TicketCompanyDefaults tcd
            LEFT JOIN Common.Companies c ON c.Id = tcd.CompanyId
            LEFT JOIN Messaging.TicketStatuses ts ON tcd.TicketStatusDefaultId = ts.Id
            LEFT JOIN Messaging.TicketComplexities tc ON tcd.TicketComplexityDefaultId = tc.Id
            LEFT JOIN Messaging.TimeUnits tu ON tcd.TimeUnitDefaultId = tu.Id
            LEFT JOIN Admin.Areas a ON tcd.AreaDefaultId = a.Id
            LEFT JOIN Admin.Projects p ON tcd.ProjectDefaultId = p.Id
            LEFT JOIN Common.CommunicationChannels ch ON ch.Id = tcd.ChannelDefaultId
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<TicketCompanyDefaultDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<TicketCompanyDefaultDto>>
        {
            IsSuccess = true,
            Message = "System-wide ticket company default configurations retrieved successfully.",
            Data = new PagedResult<TicketCompanyDefaultDto>
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
