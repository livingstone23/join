// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Common;
using JOIN.Application.DTO.Security.RoleCompany;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleCompanies.Queries.GetRoleCompaniesPaged;

/// <summary>
/// Handler for the paged RoleCompany listing. Sanitizes page/pageSize (clamp 1..100 / >=1),
/// validates the tenant context, and delegates to <see cref="IRoleCompanyRepository.GetPagedAsync"/>.
/// </summary>
public sealed class GetRoleCompaniesPagedQueryHandler(
    IRoleCompanyRepository roleCompanyRepository,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetRoleCompaniesPagedQuery, Response<PagedResult<RoleCompanyListItemDto>>>
{
    private const int MaxPageSize = 100;
    private const int MinPageSize = 1;
    private const int DefaultPageSize = 20;

    private readonly IRoleCompanyRepository _roleCompanyRepository = roleCompanyRepository ?? throw new ArgumentNullException(nameof(roleCompanyRepository));
    private readonly ICurrentUserService _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));

    public async Task<Response<PagedResult<RoleCompanyListItemDto>>> Handle(
        GetRoleCompaniesPagedQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentUserService.CompanyId;
        if (tenantId == Guid.Empty)
        {
            return Response<PagedResult<RoleCompanyListItemDto>>.Error(
                "INVALID_COMPANY_ID",
                new[] { "El token no contiene un CompanyId válido." });
        }

        var sanitizedPage = request.Page < 1 ? 1 : request.Page;
        var requestedPageSize = request.PageSize < MinPageSize ? DefaultPageSize : request.PageSize;
        var sanitizedPageSize = Math.Min(requestedPageSize, MaxPageSize);

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
