// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Common;
using JOIN.Application.DTO.Security.RoleUsers;
using MediatR;

namespace JOIN.Application.UseCases.Security.Roles.Queries.GetUsersByRoleId;

/// <summary>
/// Query for the paged list of users assigned to a given role within the caller's tenant
/// (i.e. users whose Security.UserRoleCompanies row matches RoleId and CompanyId from the token).
/// Powers the "usuarios afectados" preview shown by the UI before saving role changes.
/// </summary>
public sealed record GetUsersByRoleIdQuery(
    Guid RoleId,
    int Page = 1,
    int PageSize = 20) : IRequest<Response<PagedResult<RoleAffectedUserDto>>>;
