// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Common;
using JOIN.Application.DTO.Security.RoleUsers;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Security.Roles.Queries.GetUsersByRoleId;

/// <summary>
/// Handler for the "usuarios afectados por rol" preview query.
/// Validates the tenant context, confirms the role exists and is not soft-deleted,
/// sanitizes page/pageSize via the shared <see cref="PaginationSettings"/>, and delegates to
/// <see cref="IRoleCompanyRepository.GetUsersByRoleIdPagedAsync"/>.
/// </summary>
public sealed class GetUsersByRoleIdQueryHandler(
    IRoleCompanyRepository roleCompanyRepository,
    IRoleRepository roleRepository,
    ICurrentUserService currentUserService,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetUsersByRoleIdQuery, Response<PagedResult<RoleAffectedUserDto>>>
{
    private readonly IRoleCompanyRepository _roleCompanyRepository = roleCompanyRepository ?? throw new ArgumentNullException(nameof(roleCompanyRepository));
    private readonly IRoleRepository _roleRepository = roleRepository ?? throw new ArgumentNullException(nameof(roleRepository));
    private readonly ICurrentUserService _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<RoleAffectedUserDto>>> Handle(
        GetUsersByRoleIdQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantResolver.Resolve(_currentUserService, request.CompanyId);
        if (tenantId == Guid.Empty)
        {
            return Response<PagedResult<RoleAffectedUserDto>>.Error(
                "INVALID_COMPANY_ID",
                new[] { "El token no contiene un CompanyId válido." });
        }

        if (!await _roleRepository.ExistsAndActiveAsync(request.RoleId, cancellationToken))
        {
            return Response<PagedResult<RoleAffectedUserDto>>.Error(
                "Rol no encontrado o inactivo.",
                new[] { "El rol indicado no existe o se encuentra inactivo." });
        }

        var (sanitizedPage, sanitizedPageSize) = _paginationSettings.Sanitize(request.Page, request.PageSize);

        var (items, total) = await _roleCompanyRepository.GetUsersByRoleIdPagedAsync(
            request.RoleId,
            tenantId,
            sanitizedPage,
            sanitizedPageSize,
            cancellationToken);

        var pagedResult = new PagedResult<RoleAffectedUserDto>
        {
            Items = items,
            PageNumber = sanitizedPage,
            PageSize = sanitizedPageSize,
            TotalCount = total,
            TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)sanitizedPageSize)
        };

        return new Response<PagedResult<RoleAffectedUserDto>>
        {
            IsSuccess = true,
            Message = "Users affected by role retrieved successfully.",
            Data = pagedResult
        };
    }
}
