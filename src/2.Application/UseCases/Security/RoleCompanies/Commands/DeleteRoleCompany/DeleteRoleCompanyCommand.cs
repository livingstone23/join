// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleCompanies.Commands.DeleteRoleCompany;

/// <summary>
/// Command that soft-deletes an existing RoleCompany link by stamping GcRecord with the yyyyMMdd UTC int.
/// </summary>
public sealed record DeleteRoleCompanyCommand(Guid Id) : IRequest<Response<bool>>;
