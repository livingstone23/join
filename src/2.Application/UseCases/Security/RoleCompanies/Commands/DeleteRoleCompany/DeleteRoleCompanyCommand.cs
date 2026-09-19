// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleCompanies.Commands.DeleteRoleCompany;

/// <summary>
/// Command that soft-deletes an existing RoleCompany link by stamping GcRecord with the yyyyMMdd UTC int.
/// <see cref="CompanyId"/> overrides the token's tenant only for a real <c>SuperAdmin</c>
/// (see <see cref="JOIN.Application.Common.TenantResolver"/>).
/// </summary>
public sealed record DeleteRoleCompanyCommand(Guid Id, Guid? CompanyId = null) : IRequest<Response<bool>>;
