using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Common.Companies.Queries;

/// <summary>
/// Handles paginated company queries using Dapper.
/// SPEC 38: SuperAdmin sees every active company; everyone else (SuperAdminCompany, Manager, …)
/// is forced to the CompanyId of the token — a SuperAdminCompany must not be able to enumerate
/// other companies in the platform.
/// </summary>
/// <param name="connectionFactory">Factory used to create DB-agnostic read connections.</param>
/// <param name="paginationOptions">Configurable pagination defaults shared across paged endpoints.</param>
/// <param name="currentUserService">Resolves the caller's tenant and role for the tenant-scope guard.</param>
public class GetCompaniesPagedQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCompaniesPagedQuery, Response<PagedResult<CompanyListItemDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    /// <summary>
    /// Retrieves a paginated list of active companies.
    /// </summary>
    public async Task<Response<PagedResult<CompanyListItemDto>>> Handle(GetCompaniesPagedQuery request, CancellationToken cancellationToken)
    {
        var (sanitizedPageNumber, sanitizedPageSize) = _paginationSettings.Sanitize(request.PageNumber, request.PageSize);
        var offset = (sanitizedPageNumber - 1) * sanitizedPageSize;

        using var connection = connectionFactory.CreateConnection();

        var parameters = new DynamicParameters();
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", sanitizedPageSize);

        var whereBuilder = new StringBuilder("WHERE c.GcRecord = 0");

        // SPEC 38: non-SuperAdmin is locked to its own tenant. The endpoint stays open to the
        // SuperAdminCompany role in the controller, but the query result is restricted here so
        // a SuperAdminCompany of A cannot list companies B, C, … from the platform.
        var isSuperAdmin = currentUserService.IsInRole("SuperAdmin");
        if (!isSuperAdmin)
        {
            var tenantId = currentUserService.CompanyId;
            if (tenantId == Guid.Empty)
            {
                return Response<PagedResult<CompanyListItemDto>>.Error(
                    "INVALID_COMPANY_ID",
                    ["Authenticated token must contain a valid CompanyId claim."]);
            }

            whereBuilder.Append(" AND c.Id = @TenantId");
            parameters.Add("TenantId", tenantId);
        }

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
            LIMIT @PageSize OFFSET @Offset;

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
}
