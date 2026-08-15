using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Roles.Queries.GetRoleById;

/// <summary>
/// Handler that returns a single role or a 404-flavored error response when the role is missing or soft-deleted.
/// The caller's <c>CompanyId</c> is forwarded to the repository so <c>PermissionsCount</c> stays tenant-scoped.
/// </summary>
public sealed class GetRoleByIdQueryHandler(
    IRoleRepository roleRepository,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetRoleByIdQuery, Response<RoleDto>>
{
    private readonly IRoleRepository _roleRepository = roleRepository ?? throw new ArgumentNullException(nameof(roleRepository));
    private readonly ICurrentUserService _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));

    public async Task<Response<RoleDto>> Handle(GetRoleByIdQuery request, CancellationToken cancellationToken)
    {
        var companyId = _currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<RoleDto>.Error("No se pudo identificar la compania del usuario actual para consultar el rol.");
        }

        var role = await _roleRepository.GetByIdAsync(request.Id, companyId, cancellationToken);
        if (role is null)
        {
            return Response<RoleDto>.Error("Rol no encontrado o inactivo.");
        }

        return new Response<RoleDto>
        {
            IsSuccess = true,
            Message = "Role retrieved successfully.",
            Data = role
        };
    }
}
