// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Common;
using JOIN.Application.DTO.Security.RoleCompany;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleCompanies.Queries.GetRoleCompanyById;

/// <summary>
/// Handler that returns a single RoleCompany link or an error response when missing, soft-deleted, or cross-tenant.
/// Tenant scope comes exclusively from <see cref="ICurrentUserService.CompanyId"/>; never from the caller.
/// </summary>
public sealed class GetRoleCompanyByIdQueryHandler(
    IRoleCompanyRepository roleCompanyRepository,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetRoleCompanyByIdQuery, Response<RoleCompanyDto>>
{
    private readonly IRoleCompanyRepository _roleCompanyRepository = roleCompanyRepository ?? throw new ArgumentNullException(nameof(roleCompanyRepository));
    private readonly ICurrentUserService _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));

    public async Task<Response<RoleCompanyDto>> Handle(GetRoleCompanyByIdQuery request, CancellationToken cancellationToken)
    {
        var tenantId = _currentUserService.CompanyId;
        if (tenantId == Guid.Empty)
        {
            return Response<RoleCompanyDto>.Error(
                "INVALID_COMPANY_ID",
                new[] { "El token no contiene un CompanyId válido." });
        }

        var dto = await _roleCompanyRepository.GetByIdAsync(request.Id, tenantId, cancellationToken);
        if (dto is null)
        {
            return Response<RoleCompanyDto>.Error(
                "ROLE_COMPANY_NOT_FOUND",
                new[] { "No se encontró el vínculo rol-empresa para la compañía del token." });
        }

        return new Response<RoleCompanyDto>
        {
            IsSuccess = true,
            Message = "RoleCompany retrieved successfully.",
            Data = dto
        };
    }
}
