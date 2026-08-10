using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.Mappings.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Roles.Commands.UpdateRole;

/// <summary>
/// Handler that updates an existing ApplicationRole. Enforces three rules for system-default roles
/// (no rename, no flag demotion) and blocks promoting a custom role to system default.
/// </summary>
public sealed class UpdateRoleCommandHandler(
    IUnitOfWork unitOfWork,
    IRoleRepository roleRepository,
    IRoleMapper roleMapper,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateRoleCommand, Response<RoleDto>>
{
    public async Task<Response<RoleDto>> Handle(UpdateRoleCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<RoleDto>.Error("No se pudo identificar la compania del usuario actual para actualizar el rol.");
        }

        var existing = await roleRepository.GetByIdForUpdateAsync(request.Id, cancellationToken);
        if (existing is null)
        {
            return Response<RoleDto>.Error("Rol no encontrado o inactivo.");
        }

        var nameChanged = request.Name != null;
        var trimmedName = nameChanged ? request.Name!.Trim() : existing.Name;
        var newNormalizedName = nameChanged ? trimmedName.ToUpperInvariant() : existing.NormalizedName;

        // System-default role cannot be renamed.
        if (existing.IsSystemDefault && nameChanged
            && !string.Equals(existing.NormalizedName, newNormalizedName, StringComparison.Ordinal))
        {
            return Response<RoleDto>.Error("No se puede modificar el nombre de un rol del sistema. Solo es editable su descripcion.");
        }

        // System-default role cannot be demoted.
        if (existing.IsSystemDefault && !request.IsSystemDefault)
        {
            return Response<RoleDto>.Error("No se puede dejar de marcar un rol del sistema. Cree un rol personalizado equivalente en su lugar.");
        }

        // Custom role cannot be promoted to system default.
        if (!existing.IsSystemDefault && request.IsSystemDefault)
        {
            return Response<RoleDto>.Error("No se puede ascender un rol personalizado a rol del sistema. Esta operacion requiere un flujo administrativo separado.");
        }

        // Rename collision against another role (only when the caller actually sent a new name).
        if (nameChanged
            && !string.Equals(existing.NormalizedName, newNormalizedName, StringComparison.Ordinal)
            && await roleRepository.ExistsByNameExceptIdAsync(newNormalizedName, existing.Id, cancellationToken))
        {
            return Response<RoleDto>.Error($"Ya existe otro rol con el nombre '{trimmedName}'.");
        }

        existing.Name = trimmedName;
        existing.NormalizedName = newNormalizedName;
        existing.Description = request.Description;
        existing.IsSystemDefault = request.IsSystemDefault;
        existing.LastModified = DateTime.UtcNow;
        existing.LastModifiedBy = currentUserService.UserId;

        await roleRepository.UpdateAsync(existing, cancellationToken);
        var affected = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (affected <= 0)
        {
            return Response<RoleDto>.Error("No se pudo actualizar el rol. Intente nuevamente.");
        }

        var dto = roleMapper.FromEntity(existing);
        return new Response<RoleDto>
        {
            IsSuccess = true,
            Message = "Role updated successfully.",
            Data = dto
        };
    }
}
