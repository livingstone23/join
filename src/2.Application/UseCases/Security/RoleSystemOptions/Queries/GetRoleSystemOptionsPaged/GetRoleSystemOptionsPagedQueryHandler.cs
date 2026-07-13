using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Queries;

/// <summary>
/// Handles tenant-scoped paged retrieval of RoleSystemOption rules.
/// </summary>
public sealed class GetRoleSystemOptionsPagedQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetRoleSystemOptionsPagedQuery, Response<PagedResult<RoleSystemOptionListItemDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<RoleSystemOptionListItemDto>>> Handle(
        GetRoleSystemOptionsPagedQuery request,
        CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<PagedResult<RoleSystemOptionListItemDto>>.Error(
                "INVALID_COMPANY_ID",
                ["A valid company context is required."]);
        }

        var (sanitizedPageNumber, sanitizedPageSize, offset) = RoleSystemOptionQuerySql.SanitizePagination(
            request.PageNumber,
            request.PageSize,
            _paginationSettings);

        var (whereClause, parameters) = RoleSystemOptionQuerySql.BuildWhereClause(new RoleSystemOptionQueryFilters(
            CompanyId: companyId,
            RequireCompanyFilter: true,
            RoleId: request.RoleId,
            SystemOptionId: request.SystemOptionId,
            RoleName: request.RoleName,
            SystemOptionName: request.SystemOptionName,
            CompanyName: request.CompanyName,
            CanRead: request.CanRead,
            CanCreate: request.CanCreate,
            CanUpdate: request.CanUpdate,
            CanDelete: request.CanDelete));

        parameters.Add("Offset", offset);
        parameters.Add("PageSize", sanitizedPageSize);

        using var connection = connectionFactory.CreateConnection();

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(
                RoleSystemOptionQuerySql.BuildPagedSql(whereClause),
                parameters,
                cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<RoleSystemOptionListItemDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<RoleSystemOptionListItemDto>>
        {
            IsSuccess = true,
            Message = "Role system options retrieved successfully.",
            Data = new PagedResult<RoleSystemOptionListItemDto>
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
