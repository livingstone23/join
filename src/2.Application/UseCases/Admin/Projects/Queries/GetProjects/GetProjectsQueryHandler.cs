using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Admin.Projects.Queries;

/// <summary>
/// Handles tenant-scoped project list queries using Dapper for high-performance reads.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
/// <param name="paginationOptions">Configurable pagination defaults for the project listing endpoint.</param>
public sealed class GetProjectsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetProjectsQuery, Response<PagedResult<ProjectDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    /// <summary>
    /// Retrieves a paginated list of active projects that belong to the requested company.
    /// </summary>
    /// <param name="request">The tenant-scoped list query.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized paged response containing the matching projects.</returns>
    public async Task<Response<PagedResult<ProjectDto>>> Handle(GetProjectsQuery request, CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Response<PagedResult<ProjectDto>>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]);
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

        var whereBuilder = new StringBuilder("WHERE p.CompanyId = @CompanyId AND p.GcRecord = 0");

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            whereBuilder.Append(" AND p.Name LIKE @Name");
            parameters.Add("Name", $"%{request.Name.Trim()}%");
        }

        if (request.EntityStatusId.HasValue && request.EntityStatusId.Value != Guid.Empty)
        {
            whereBuilder.Append(" AND p.EntityStatusId = @EntityStatusId");
            parameters.Add("EntityStatusId", request.EntityStatusId.Value);
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                p.Id,
                p.CompanyId,
                c.Name AS CompanyName,
                p.Name,
                p.EntityStatusId,
                es.Name AS EntityStatusName,
                p.Created AS CreatedAt
            FROM Admin.Projects p
            INNER JOIN Admin.EntityStatuses es
                ON es.Id = p.EntityStatusId
               AND es.GcRecord = 0
            INNER JOIN Common.Companies c
                ON c.Id = p.CompanyId
               AND c.GcRecord = 0
            {whereClause}
            ORDER BY p.Created DESC, p.Name ASC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Admin.Projects p
            INNER JOIN Common.Companies c
                ON c.Id = p.CompanyId
               AND c.GcRecord = 0
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<ProjectDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<ProjectDto>>
        {
            IsSuccess = true,
            Message = "Projects retrieved successfully.",
            Data = new PagedResult<ProjectDto>
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