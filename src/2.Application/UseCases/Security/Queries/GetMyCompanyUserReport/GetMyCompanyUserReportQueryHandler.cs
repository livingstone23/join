using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Security.Queries.GetSystemWideUserReport;
using MediatR;

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
public sealed class GetMyCompanyUserReportQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyCompanyUserReportQuery, Response<PagedResult<UserManagementReportDto>>>
{
    /// <summary>
    /// Retrieves a paginated slice of the report restricted to the company resolved
    /// from the authenticated token. Page-size is clamped to <c>[1, 50]</c>; default
    /// is 10 when the caller omits it.
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

        var pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
        var pageSize = request.PageSize < 1
            ? UserManagementReportQueryHelper.DefaultPageSize
            : Math.Min(request.PageSize, UserManagementReportQueryHelper.MaxPageSize);

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