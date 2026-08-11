// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Common;
using JOIN.Application.DTO.Security.RoleCompany;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleCompanies.Commands.CreateRoleCompany;

/// <summary>
/// Handler that creates a new RoleCompany link.
/// CompanyId is always resolved from <see cref="ICurrentUserService.CompanyId"/> (never the body).
/// Validation order: tenant → role exists+active → no duplicate link → persist → reload via Dapper.
/// </summary>
public sealed class CreateRoleCompanyCommandHandler(
    IUnitOfWork unitOfWork,
    IRoleCompanyRepository roleCompanyRepository,
    IRoleRepository roleRepository,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateRoleCompanyCommand, Response<RoleCompanyDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    private readonly IRoleCompanyRepository _roleCompanyRepository = roleCompanyRepository ?? throw new ArgumentNullException(nameof(roleCompanyRepository));
    private readonly IRoleRepository _roleRepository = roleRepository ?? throw new ArgumentNullException(nameof(roleRepository));
    private readonly ICurrentUserService _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));

    public async Task<Response<RoleCompanyDto>> Handle(CreateRoleCompanyCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _currentUserService.CompanyId;
        if (tenantId == Guid.Empty)
        {
            return Response<RoleCompanyDto>.Error(
                "INVALID_COMPANY_ID",
                new[] { "El token no contiene un CompanyId válido." });
        }

        // RoleId must exist and be active (GcRecord == 0). We can't tell "missing" from "soft-deleted"
        // without a separate query, but both are invalid for a new link — treat soft-deleted as 400 ROLE_INACTIVE.
        // GetByIdAsync on the role repo returns null when missing or soft-deleted (it filters GcRecord = 0).
        // We do an extra ExistsAndActiveAsync for clarity of error code.
        var existingRole = await _roleRepository.GetByIdAsync(request.RoleId, cancellationToken);
        if (existingRole is null)
        {
            return Response<RoleCompanyDto>.Error(
                "ROLE_NOT_FOUND",
                new[] { $"No existe el rol con id '{request.RoleId}'." });
        }

        // Role exists but might be soft-deleted; ExistsAndActiveAsync gives us a clean bit.
        if (!await _roleRepository.ExistsAndActiveAsync(request.RoleId, cancellationToken))
        {
            return Response<RoleCompanyDto>.Error(
                "ROLE_INACTIVE",
                new[] { $"El rol con id '{request.RoleId}' está inactivo y no puede vincularse." });
        }

        if (await _roleCompanyRepository.ExistsActiveLinkAsync(request.RoleId, tenantId, cancellationToken))
        {
            return Response<RoleCompanyDto>.Error(
                "ROLE_COMPANY_DUPLICATE",
                new[] { "Ya existe un vínculo activo entre este rol y la compañía del token." });
        }

        var entity = new RoleCompany
        {
            RoleId = request.RoleId,
            CompanyId = tenantId,
            Created = DateTime.UtcNow,
            CreatedBy = _currentUserService.UserId,
            GcRecord = 0
        };

        await _roleCompanyRepository.AddAsync(entity, cancellationToken);
        var affected = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (affected <= 0)
        {
            return Response<RoleCompanyDto>.Error(
                "ROLE_COMPANY_CREATE_FAILED",
                new[] { "No se pudo crear el vínculo rol-empresa. Intente nuevamente." });
        }

        // Reload via Dapper so the response carries RoleName + IsSystemDefault from Security.Roles.
        var dto = await _roleCompanyRepository.GetByIdAsync(entity.Id, tenantId, cancellationToken);
        if (dto is null)
        {
            return Response<RoleCompanyDto>.Error(
                "ROLE_COMPANY_NOT_FOUND",
                new[] { "El vínculo se creó pero no se pudo recargar para la respuesta." });
        }

        return new Response<RoleCompanyDto>
        {
            IsSuccess = true,
            Message = "RoleCompany created successfully.",
            Data = dto
        };
    }
}
