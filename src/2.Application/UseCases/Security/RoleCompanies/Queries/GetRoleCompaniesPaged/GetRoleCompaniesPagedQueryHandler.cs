// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Common;
using JOIN.Application.DTO.Security.RoleCompany;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Security.RoleCompanies.Queries.GetRoleCompaniesPaged;

/// <summary>
/// Handler for the paged RoleCompany listing. Sanitizes page/pageSize via the shared
/// <see cref="PaginationSettings"/>, validates the tenant context, and delegates to
/// <see cref="IRoleCompanyRepository.GetPagedAsync"/>.
/// </summary>
public sealed class GetRoleCompaniesPagedQueryHandler(
    IRoleCompanyRepository roleCompanyRepository,
    ICurrentUserService currentUserService,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetRoleCompaniesPagedQuery, Response<PagedResult<RoleCompanyListItemDto>>>
{
    private readonly IRoleCompanyRepository _roleCompanyRepository = roleCompanyRepository ?? throw new ArgumentNullException(nameof(roleCompanyRepository));
    private readonly ICurrentUserService _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<RoleCompanyListItemDto>>> Handle(
        GetRoleCompaniesPagedQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantResolver.Resolve(_currentUserService, request.CompanyId);
        if (tenantId == Guid.Empty)
        {
            return Response<PagedResult<RoleCompanyListItemDto>>.Error(
                "INVALID_COMPANY_ID",
                new[] { "El token no contiene un CompanyId válido." });
        }

        var (sanitizedPage, sanitizedPageSize) = _paginationSettings.Sanitize(request.Page, request.PageSize);

        var (items, total) = await _roleCompanyRepository.GetPagedAsync(
            tenantId,
            request.RoleId,
            request.IsActive,
            sanitizedPage,
            sanitizedPageSize,
            cancellationToken);

        var pagedResult = new PagedResult<RoleCompanyListItemDto>
        {
            Items = items,
            PageNumber = sanitizedPage,
            PageSize = sanitizedPageSize,
            TotalCount = total,
            TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)sanitizedPageSize)
        };

        return new Response<PagedResult<RoleCompanyListItemDto>>
        {
            IsSuccess = true,
            Message = "RoleCompanies retrieved successfully.",
            Data = pagedResult
        };
    }
}
