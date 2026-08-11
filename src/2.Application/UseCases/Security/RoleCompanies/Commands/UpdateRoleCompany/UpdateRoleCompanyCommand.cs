// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Common;
using JOIN.Application.DTO.Security.RoleCompany;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleCompanies.Commands.UpdateRoleCompany;

/// <summary>
/// Command that changes the RoleId of an existing RoleCompany link.
/// CompanyId is preserved from the caller's token; only the role changes.
/// </summary>
public sealed record UpdateRoleCompanyCommand(Guid Id, Guid RoleId) : IRequest<Response<RoleCompanyDto>>;
