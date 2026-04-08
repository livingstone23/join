using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Queries;

/// <summary>
/// Handles tenant-scoped company module list queries using Dapper for high-performance reads.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
/// <param name="paginationOptions">Configurable pagination defaults for the company module listing endpoint.</param>
public sealed class GetCompanyModulesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetCompanyModulesQuery, Response<PagedResult<CompanyModuleListItemDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    /// <summary>
    /// Retrieves a paginated list of active company module assignments for the requested tenant.
    /// </summary>
    /// <param name="request">The tenant-scoped list query.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized paged response containing the matching module assignments.</returns>
    public async Task<Response<PagedResult<CompanyModuleListItemDto>>> Handle(GetCompanyModulesQuery request, CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Response<PagedResult<CompanyModuleListItemDto>>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]);
        }

        if (request.CreatedFrom.HasValue && request.CreatedTo.HasValue && request.CreatedFrom.Value.Date > request.CreatedTo.Value.Date)
        {
            return Response<PagedResult<CompanyModuleListItemDto>>.Error(
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
        parameters.Add("CompanyId", request.CompanyId);
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", sanitizedPageSize);

        var whereBuilder = new StringBuilder("WHERE cm.CompanyId = @CompanyId AND cm.GcRecord = 0");

        if (!string.IsNullOrWhiteSpace(request.CompanyName))
        {
            whereBuilder.Append(" AND c.Name LIKE @CompanyName");
            parameters.Add("CompanyName", $"%{request.CompanyName.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(request.ModuleName))
        {
            whereBuilder.Append(" AND sm.Name LIKE @ModuleName");
            parameters.Add("ModuleName", $"%{request.ModuleName.Trim()}%");
        }

        if (request.CreatedFrom.HasValue)
        {
            whereBuilder.Append(" AND cm.Created >= @CreatedFrom");
            parameters.Add("CreatedFrom", request.CreatedFrom.Value.Date);
        }

        if (request.CreatedTo.HasValue)
        {
            whereBuilder.Append(" AND cm.Created < @CreatedToExclusive");
            parameters.Add("CreatedToExclusive", request.CreatedTo.Value.Date.AddDays(1));
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                cm.Id,
                cm.CompanyId,
                c.Name AS CompanyName,
                cm.ModuleId,
                sm.Name AS ModuleName,
                cm.IsActive,
                cm.Created AS CreatedAt
            FROM Admin.CompanyModules cm
            INNER JOIN Common.Companies c
                ON c.Id = cm.CompanyId
               AND c.GcRecord = 0
            INNER JOIN Admin.SystemModules sm
                ON sm.Id = cm.ModuleId
               AND sm.GcRecord = 0
            {whereClause}
            ORDER BY cm.Created DESC, sm.Name ASC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Admin.CompanyModules cm
            INNER JOIN Common.Companies c
                ON c.Id = cm.CompanyId
               AND c.GcRecord = 0
            INNER JOIN Admin.SystemModules sm
                ON sm.Id = cm.ModuleId
               AND sm.GcRecord = 0
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<CompanyModuleListItemDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<CompanyModuleListItemDto>>
        {
            IsSuccess = true,
            Message = "Company modules retrieved successfully.",
            Data = new PagedResult<CompanyModuleListItemDto>
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
