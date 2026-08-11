// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Common;
using JOIN.Application.DTO.Security.RoleCompany;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleCompanies.Commands.UpdateRoleCompany;

/// <summary>
/// Handler that reassigns the RoleId of an existing RoleCompany link.
/// CompanyId is preserved from the caller's token; cross-tenant ids return 404, never 403.
/// </summary>
public sealed class UpdateRoleCompanyCommandHandler(
    IUnitOfWork unitOfWork,
    IRoleCompanyRepository roleCompanyRepository,
    IRoleRepository roleRepository,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateRoleCompanyCommand, Response<RoleCompanyDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    private readonly IRoleCompanyRepository _roleCompanyRepository = roleCompanyRepository ?? throw new ArgumentNullException(nameof(roleCompanyRepository));
    private readonly IRoleRepository _roleRepository = roleRepository ?? throw new ArgumentNullException(nameof(roleRepository));
    private readonly ICurrentUserService _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));

    public async Task<Response<RoleCompanyDto>> Handle(UpdateRoleCompanyCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _currentUserService.CompanyId;
        if (tenantId == Guid.Empty)
        {
            return Response<RoleCompanyDto>.Error(
                "INVALID_COMPANY_ID",
                new[] { "El token no contiene un CompanyId válido." });
        }

        var existing = await _roleCompanyRepository.GetByIdForUpdateAsync(request.Id, tenantId, cancellationToken);
        if (existing is null)
        {
            return Response<RoleCompanyDto>.Error(
                "ROLE_COMPANY_NOT_FOUND",
                new[] { "No se encontró el vínculo rol-empresa para la compañía del token." });
        }

        var existingRole = await _roleRepository.GetByIdAsync(request.RoleId, cancellationToken);
        if (existingRole is null)
        {
            return Response<RoleCompanyDto>.Error(
                "ROLE_NOT_FOUND",
                new[] { $"No existe el rol con id '{request.RoleId}'." });
        }

        if (!await _roleRepository.ExistsAndActiveAsync(request.RoleId, cancellationToken))
        {
            return Response<RoleCompanyDto>.Error(
                "ROLE_INACTIVE",
                new[] { $"El rol con id '{request.RoleId}' está inactivo y no puede vincularse." });
        }

        // Only check for duplicates when the RoleId actually changes; otherwise this is a no-op update
        // and the unique filtered index already guarantees uniqueness for the (Role, Company) pair.
        if (existing.RoleId != request.RoleId
            && await _roleCompanyRepository.ExistsActiveLinkAsync(request.RoleId, tenantId, cancellationToken))
        {
            return Response<RoleCompanyDto>.Error(
                "ROLE_COMPANY_DUPLICATE",
                new[] { "Ya existe otro vínculo activo entre este rol y la compañía del token." });
        }

        existing.RoleId = request.RoleId;
        existing.LastModified = DateTime.UtcNow;
        existing.LastModifiedBy = _currentUserService.UserId;

        await _roleCompanyRepository.UpdateAsync(existing, cancellationToken);
        var affected = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (affected <= 0)
        {
            return Response<RoleCompanyDto>.Error(
                "ROLE_COMPANY_UPDATE_FAILED",
                new[] { "No se pudo actualizar el vínculo rol-empresa. Intente nuevamente." });
        }

        // Reload via Dapper so the response carries RoleName + IsSystemDefault.
        var dto = await _roleCompanyRepository.GetByIdAsync(existing.Id, tenantId, cancellationToken);
        if (dto is null)
        {
            return Response<RoleCompanyDto>.Error(
                "ROLE_COMPANY_NOT_FOUND",
                new[] { "El vínculo se actualizó pero no se pudo recargar para la respuesta." });
        }

        return new Response<RoleCompanyDto>
        {
            IsSuccess = true,
            Message = "RoleCompany updated successfully.",
            Data = dto
        };
    }
}
