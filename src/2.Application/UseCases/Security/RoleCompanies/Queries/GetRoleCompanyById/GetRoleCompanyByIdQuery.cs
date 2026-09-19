// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Common;
using JOIN.Application.DTO.Security.RoleCompany;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleCompanies.Queries.GetRoleCompanyById;

/// <summary>
/// Query for fetching a single RoleCompany junction by id, scoped to the caller's tenant.
/// <see cref="CompanyId"/> overrides the token's tenant only for a real <c>SuperAdmin</c>
/// (see <see cref="JOIN.Application.Common.TenantResolver"/>).
/// </summary>
public sealed record GetRoleCompanyByIdQuery(Guid Id, Guid? CompanyId = null) : IRequest<Response<RoleCompanyDto>>;
