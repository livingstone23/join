using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.Mappings.Security;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JOIN.Application.UseCases.Security.Roles.Commands.CreateRole;

/// <summary>
/// Handler that creates a new ApplicationRole after enforcing uniqueness on the normalized name
/// and rejecting unauthenticated callers (CompanyId == Guid.Empty).
/// When <see cref="CreateRoleCommand.CloneFromRoleId"/> is supplied, the handler also
/// copies the origin role's active <c>RoleSystemOption</c> rows for the caller's tenant
/// onto the new role — same flags, same OrderMenu, same IsVisibleMenu — inside the same
/// outer transaction (atomic).
/// </summary>
public sealed class CreateRoleCommandHandler(
    IUnitOfWork unitOfWork,
    IRoleRepository roleRepository,
    IRoleMapper roleMapper,
    ICurrentUserService currentUserService,
    IAuditLogger auditLogger,
    ILogger<CreateRoleCommandHandler> logger)
    : IRequestHandler<CreateRoleCommand, Response<RoleDto>>
{
    public async Task<Response<RoleDto>> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<RoleDto>.Error("No se pudo identificar la compania del usuario actual para crear el rol.");
        }

        var trimmedName = (request.Name ?? string.Empty).Trim();
        var normalizedName = trimmedName.ToUpperInvariant();

        if (await roleRepository.ExistsByNameAsync(normalizedName, cancellationToken))
        {
            return Response<RoleDto>.Error($"Ya existe un rol con el nombre '{trimmedName}'.");
        }

        // Validate clone origin (if any) BEFORE we insert anything, so an invalid clone
        // never leaves a half-created role behind.
        IReadOnlyList<RoleSystemOption> originPermissions = Array.Empty<RoleSystemOption>();
        if (request.CloneFromRoleId.HasValue)
        {
            var originId = request.CloneFromRoleId.Value;
            var tenantId = currentUserService.CompanyId;

            // Roles is a global catalog (Identity); tenant isolation lives in RoleSystemOption.
            // We can't trust RoleDto.GcRecord here — instead we read the origin's permission
            // rows for the caller's tenant. An empty list is a legitimate "role has no
            // permissions in this tenant" case AND the "cross-tenant clone" case. To
            // distinguish them we re-read the origin's RoleDto (now carrying PermissionsCount).
            var originRole = await roleRepository.GetByIdAsync(originId, tenantId, cancellationToken);
            if (originRole is null)
            {
                return Response<RoleDto>.Error("ROLE_NOT_FOUND", [$"No existe el rol origen con id '{originId}'."]);
            }

            originPermissions = await unitOfWork.RoleSystemOptions
                .GetActiveByRoleAndCompanyAsync(originId, tenantId, cancellationToken);

            // Cross-tenant detection: if the origin globally has permissions but none of
            // them belong to the caller's tenant, the clone would silently create a role
            // with zero permissions — surface that as ROLE_NOT_FOUND instead.
            if (originPermissions.Count == 0 && originRole.PermissionsCount > 0)
            {
                logger.LogWarning(
                    "Clone from role {OriginId} rejected: {PermissionsCount} permissions exist in another tenant, none in caller tenant {TenantId}.",
                    originId, originRole.PermissionsCount, tenantId);
                return Response<RoleDto>.Error(
                    "ROLE_NOT_FOUND",
                    [$"El rol origen no tiene permisos configurados para la compania del usuario actual."]);
            }
        }

        var entity = new ApplicationRole
        {
            Name = trimmedName,
            NormalizedName = normalizedName,
            Description = request.Description,
            IsSystemDefault = request.IsSystemDefault,
            CreatedBy = currentUserService.UserId,
            Created = DateTime.UtcNow,
            GcRecord = 0
        };

        await roleRepository.AddAsync(entity, cancellationToken);
        var affected = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (affected <= 0)
        {
            return Response<RoleDto>.Error("No se pudo crear el rol. Intente nuevamente.");
        }

        // Clone RoleSystemOption rows from origin onto the new role. Same outer transaction
        // (TransactionBehavior) keeps this atomic: if any InsertAsync fails, the new role
        // is rolled back alongside it — no orphan roles.
        if (request.CloneFromRoleId.HasValue && originPermissions.Count > 0)
        {
            var tenantId = currentUserService.CompanyId;
            var newPermissions = originPermissions.Select(p => new RoleSystemOption
            {
                CompanyId = tenantId,
                RoleId = entity.Id,
                SystemOptionId = p.SystemOptionId,
                CanRead = p.CanRead,
                CanCreate = p.CanCreate,
                CanUpdate = p.CanUpdate,
                CanDelete = p.CanDelete,
                CanDownload = p.CanDownload,
                CanExport = p.CanExport,
                CanExecute = p.CanExecute,
                IsVisibleMenu = p.IsVisibleMenu,
                OrderMenu = p.OrderMenu,
                GcRecord = 0,
                CreatedBy = currentUserService.UserId,
                Created = DateTime.UtcNow
            }).ToList();

            foreach (var permission in newPermissions)
            {
                await unitOfWork.RoleSystemOptions.InsertAsync(permission);
            }
            var cloneAffected = await unitOfWork.SaveChangesAsync(cancellationToken);
            if (cloneAffected <= 0)
            {
                logger.LogWarning(
                    "Clone from role {OriginId} persisted 0 permission rows for new role {NewRoleId}; rolling back.",
                    request.CloneFromRoleId, entity.Id);
                return Response<RoleDto>.Error(
                    "ROLE_CLONE_FAILED",
                    [$"No se pudieron clonar los permisos del rol origen al rol nuevo."]);
            }
        }

        // Bitácora: role created. Diff captures only the auditable scalar fields.
        await auditLogger.LogAsync(
            AuditedEntity.Role,
            entity.Id,
            AuditAction.Created,
            entityLabel: entity.Name,
            newValues: new Dictionary<string, object?>
            {
                ["Name"] = entity.Name,
                ["Description"] = entity.Description,
                ["IsSystemDefault"] = entity.IsSystemDefault
            },
            ct: cancellationToken);

        var dto = roleMapper.FromEntity(entity);
        return new Response<RoleDto>
        {
            IsSuccess = true,
            Message = "Role created successfully.",
            Data = dto
        };
    }
}
