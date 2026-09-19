// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Common;
using JOIN.Application.DTO.Security.RoleCompany;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleCompanies.Commands.UpdateRoleCompany;

/// <summary>
/// Command that changes the RoleId of an existing RoleCompany link. <see cref="CompanyId"/>
/// overrides the token's tenant only for a real <c>SuperAdmin</c>
/// (see <see cref="JOIN.Application.Common.TenantResolver"/>); otherwise preserved from the
/// caller's token, same as before.
/// </summary>
public sealed record UpdateRoleCompanyCommand(Guid Id, Guid RoleId, Guid? CompanyId = null) : IRequest<Response<RoleCompanyDto>>;
