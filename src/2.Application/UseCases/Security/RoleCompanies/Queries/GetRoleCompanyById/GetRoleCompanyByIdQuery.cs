// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Common;
using JOIN.Application.DTO.Security.RoleCompany;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleCompanies.Queries.GetRoleCompanyById;

/// <summary>
/// Query for fetching a single RoleCompany junction by id, scoped to the caller's tenant.
/// </summary>
public sealed record GetRoleCompanyByIdQuery(Guid Id) : IRequest<Response<RoleCompanyDto>>;
