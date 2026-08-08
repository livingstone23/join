using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.Mappings.Security;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Roles.Commands.CreateRole;

/// <summary>
/// Handler that creates a new ApplicationRole after enforcing uniqueness on the normalized name
/// and rejecting unauthenticated callers (CompanyId == Guid.Empty).
/// </summary>
public sealed class CreateRoleCommandHandler(
    IUnitOfWork unitOfWork,
    IRoleRepository roleRepository,
    IRoleMapper roleMapper,
    ICurrentUserService currentUserService)
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

        var dto = roleMapper.FromEntity(entity);
        return new Response<RoleDto>
        {
            IsSuccess = true,
            Message = "Role created successfully.",
            Data = dto
        };
    }
}
