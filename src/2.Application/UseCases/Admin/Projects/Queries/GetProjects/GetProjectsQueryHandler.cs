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
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService)
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
        // SPEC 41: the X-Company-Id header is only an explicit override, honored for SuperAdmin;
        // every other caller always reads the company of their token (TenantResolver).
        var companyId = TenantResolver.Resolve(currentUserService, request.CompanyId == Guid.Empty ? null : request.CompanyId);
        if (companyId == Guid.Empty)
        {
            return Response<PagedResult<ProjectDto>>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]);
        }

        var (sanitizedPageNumber, sanitizedPageSize) = _paginationSettings.Sanitize(request.PageNumber, request.PageSize);
        var offset = (sanitizedPageNumber - 1) * sanitizedPageSize;

        using var connection = connectionFactory.CreateConnection();

        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", companyId);
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", sanitizedPageSize);

        var whereBuilder = new StringBuilder("WHERE p.CompanyId = @CompanyId");
        if (!SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted))
        {
            whereBuilder.Append(" AND p.GcRecord = 0");
        }

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
                p.GcRecord,
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
            LIMIT @PageSize OFFSET @Offset;

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
}