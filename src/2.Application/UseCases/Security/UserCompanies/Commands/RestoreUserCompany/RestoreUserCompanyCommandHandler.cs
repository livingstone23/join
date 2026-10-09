using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.UserCompanies.Commands.RestoreUserCompany;

/// <summary>
/// Handles <see cref="RestoreUserCompanyCommand"/> (SPEC 41, Etapa 3). <c>RemoveUserCompany</c> deletes the
/// membership and its <see cref="UserRoleCompany"/> rows with the same stamp; restoring the membership brings
/// back those role assignments (unless the role is deleted). If the user already has another active default
/// company, the restored membership comes back without <see cref="UserCompany.IsDefault"/>. The user's
/// permission cache for the company is dropped afterwards.
/// </summary>
public sealed class RestoreUserCompanyCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer,
    IPermissionService permissionService)
    : IRequestHandler<RestoreUserCompanyCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreUserCompanyCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        // (UserId, CompanyId) is unique (unfiltered index), so there is at most one membership row.
        var membership = (await unitOfWork.GetRepository<UserCompany>()
            .GetAllIncludingDeletedAsync(uc => uc.UserId == request.UserId && uc.CompanyId == request.CompanyId))
            .FirstOrDefault();
        if (membership is null)
        {
            return Response<Guid>.Error("NOT_FOUND", [$"UserCompany for user '{request.UserId}' in company '{request.CompanyId}' was not found."]);
        }

        var response = await restorer.RestoreAsync<UserCompany>(
            membership.Id,
            request.CompanyId,
            isParentDeleted: (entity, _) => restorer.IsParentDeletedAsync<Company>(entity.CompanyId),
            beforeRestore: async (entity, _) =>
            {
                if (entity.IsDefault && await restorer.AnyAsync<UserCompany>(
                    uc => uc.GcRecord == 0 && uc.Id != entity.Id && uc.UserId == entity.UserId && uc.IsDefault))
                {
                    entity.IsDefault = false;
                }
            },
            restoreCascade: async (entity, stamp, _) =>
            {
                var assignmentRepository = unitOfWork.GetRepository<UserRoleCompany>();
                var assignments = await assignmentRepository.GetAllIncludingDeletedAsync(
                    urc => urc.UserId == entity.UserId && urc.CompanyId == entity.CompanyId && urc.GcRecord == stamp);
                foreach (var assignment in assignments)
                {
                    if (await restorer.IsParentDeletedAsync<ApplicationRole>(assignment.RoleId))
                    {
                        continue;
                    }

                    assignment.Restore();
                    await assignmentRepository.UpdateAsync(assignment);
                }

                return null;
            },
            cancellationToken: cancellationToken);

        if (response.IsSuccess)
        {
            await permissionService.InvalidateUserCacheAsync(request.CompanyId, request.UserId, cancellationToken);
        }

        return response;
    }
}
