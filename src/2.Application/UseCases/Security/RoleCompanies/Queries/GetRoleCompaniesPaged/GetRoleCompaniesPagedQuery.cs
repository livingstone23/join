// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Common;
using JOIN.Application.DTO.Security.RoleCompany;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleCompanies.Queries.GetRoleCompaniesPaged;

/// <summary>
/// Query for the paged listing of RoleCompany links scoped to the caller's tenant.
/// Optional filters narrow by RoleId and active flag.
/// </summary>
public sealed record GetRoleCompaniesPagedQuery(
    Guid? RoleId,
    bool? IsActive,
    int Page = 1,
    int PageSize = 20) : IRequest<Response<PagedResult<RoleCompanyListItemDto>>>;
