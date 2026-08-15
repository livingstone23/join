using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

/// <summary>
/// Handles soft deletion of role-system-option permission rules.
/// The tenant is always derived from the authenticated caller via <see cref="ICurrentUserService"/>.
/// </summary>
public sealed class DeleteRoleSystemOptionCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteRoleSystemOptionCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteRoleSystemOptionCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<Guid>.Error("INVALID_COMPANY_ID", ["CompanyId is required."]);
        }

        if (request.CompanyId.HasValue && request.CompanyId.Value != companyId)
        {
            return Response<Guid>.Error(
                "COMPANY_MISMATCH",
                ["CompanyId in the request does not match the authenticated tenant."]);
        }

        var repository = unitOfWork.RoleSystemOptions;
        var entity = await repository.GetTrackedActiveByIdAndCompanyAsync(request.Id, companyId, cancellationToken);
        if (entity is null)
        {
            return Response<Guid>.Error("ROLE_SYSTEM_OPTION_NOT_FOUND", ["Role system option not found."]);
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);

        var result = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the permission rule."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Role system option deleted successfully.",
            Data = entity.Id
        };
    }
}