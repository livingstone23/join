// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Common;
using JOIN.Application.DTO.Security.RoleCompany;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleCompanies.Commands.CreateRoleCompany;

/// <summary>
/// Command that creates a new RoleCompany link. CompanyId is resolved from the caller's token.
/// </summary>
public sealed record CreateRoleCompanyCommand(Guid RoleId) : IRequest<Response<RoleCompanyDto>>;
