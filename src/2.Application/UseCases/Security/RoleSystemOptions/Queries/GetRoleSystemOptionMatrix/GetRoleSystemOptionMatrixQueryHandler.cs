using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Queries.GetRoleSystemOptionMatrix;

/// <summary>
/// Handler for <see cref="GetRoleSystemOptionMatrixQuery"/>. Validates the caller's
/// tenant and the role's existence, then asks <see cref="IRoleSystemOptionsRepository.GetMatrixByRoleAsync"/>
/// to render the matrix. The repo is responsible for returning <c>null</c> when the role
/// does not exist or is soft-deleted (defense in depth — the handler also checks).
/// </summary>
public sealed class GetRoleSystemOptionMatrixQueryHandler(
    IRoleSystemOptionsRepository roleSystemOptionsRepository,
    IRoleRepository roleRepository,
    ICurrentUserService currentUserService,
    ILogger<GetRoleSystemOptionMatrixQueryHandler> logger)
    : IRequestHandler<GetRoleSystemOptionMatrixQuery, Response<RoleSystemOptionMatrixDto>>
{
    public async Task<Response<RoleSystemOptionMatrixDto>> Handle(
        GetRoleSystemOptionMatrixQuery request,
        CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<RoleSystemOptionMatrixDto>.Error(
                "TENANT_REQUIRED",
                ["CompanyId is required from the authenticated token."]);
        }

        if (!await roleRepository.ExistsAndActiveAsync(request.RoleId, cancellationToken))
        {
            return Response<RoleSystemOptionMatrixDto>.Error(
                "ROLE_NOT_FOUND",
                [$"Role {request.RoleId} not found or inactive."]);
        }

        var matrix = await roleSystemOptionsRepository.GetMatrixByRoleAsync(
            request.RoleId, companyId, cancellationToken);

        if (matrix is null)
        {
            // Repo returned null despite ExistsAndActiveAsync passing — likely a race condition
            // (role got soft-deleted between the two calls). Surface as ROLE_NOT_FOUND for the
            // client; log for ops visibility.
            logger.LogWarning(
                "GetMatrixByRoleAsync returned null for role {RoleId} despite ExistsAndActiveAsync=true.",
                request.RoleId);
            return Response<RoleSystemOptionMatrixDto>.Error(
                "ROLE_NOT_FOUND",
                [$"Role {request.RoleId} not found."]);
        }

        return new Response<RoleSystemOptionMatrixDto>
        {
            IsSuccess = true,
            Message = "Permissions matrix retrieved successfully.",
            Data = matrix
        };
    }
}