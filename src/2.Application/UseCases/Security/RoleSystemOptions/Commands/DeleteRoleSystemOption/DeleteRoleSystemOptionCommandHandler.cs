using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

/// <summary>
/// Handles soft deletion of role-system-option permission rules.
/// </summary>
public sealed class DeleteRoleSystemOptionCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteRoleSystemOptionCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteRoleSystemOptionCommand request, CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("INVALID_COMPANY_ID", ["CompanyId is required."]);
        }

        var repository = unitOfWork.RoleSystemOptions;
        var entity = await repository.GetTrackedActiveByIdAndCompanyAsync(request.Id, request.CompanyId, cancellationToken);
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
