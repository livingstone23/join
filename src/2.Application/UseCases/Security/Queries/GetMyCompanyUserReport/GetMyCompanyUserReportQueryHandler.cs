using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Security.Queries.GetSystemWideUserReport;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Security.Queries.GetMyCompanyUserReport;

/// <summary>
/// Handles the paginated user-management report scoped to the caller's tenant.
/// SPEC 28 / F7, item 21. Replaces the legacy <c>throw new UnauthorizedAccessException</c>
/// with a <c>Response.Error("TENANT_REQUIRED")</c> so the endpoint follows the
/// <see cref="JOIN.Application.Common.Response{T}"/> convention from CLAUDE.md — the
/// observable change is the HTTP status moving from 401 to 400 for that case.
/// </summary>
/// <param name="connectionFactory">Factory used to create engine-agnostic read connections.</param>
/// <param name="currentUserService">Current user context used to enforce tenant isolation.</param>
/// <param name="paginationOptions">Configurable pagination defaults shared across paged endpoints.</param>
public sealed class GetMyCompanyUserReportQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetMyCompanyUserReportQuery, Response<PagedResult<UserManagementReportDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    /// <summary>
    /// Retrieves a paginated slice of the report restricted to the company resolved
    /// from the authenticated token, sanitizing page/pageSize via the shared
    /// <see cref="PaginationSettings"/>.
    /// </summary>
    public async Task<Response<PagedResult<UserManagementReportDto>>> Handle(
        GetMyCompanyUserReportQuery request,
        CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<PagedResult<UserManagementReportDto>>.Error(
                "TENANT_REQUIRED",
                ["A tenant context is required to load the report."]);
        }

        var (pageNumber, pageSize) = _paginationSettings.Sanitize(request.PageNumber, request.PageSize);

        var (items, totalCount) = await UserManagementReportQueryHelper.ReadPagedAsync(
            connectionFactory,
            scopedCompanyId: companyId,
            targetCompanyId: null,
            fromDate: request.FromDate,
            toDate: request.ToDate,
            roleNames: request.RoleNames,
            pageNumber: pageNumber,
            pageSize: pageSize,
            search: request.Search,
            isActive: request.IsActive,
            cancellationToken: cancellationToken);

        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)pageSize);

        return new Response<PagedResult<UserManagementReportDto>>
        {
            IsSuccess = true,
            Message = "Company user report retrieved successfully.",
            Data = new PagedResult<UserManagementReportDto>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages
            }
        };
    }
}