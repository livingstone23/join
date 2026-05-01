using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings.Security.RoleSystemOption;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

/// <summary>
/// Handles updates to role-system-option permission flags.
/// </summary>
public sealed class UpdateRoleSystemOptionCommandHandler(
    IUnitOfWork unitOfWork,
    IRoleSystemOptionMapper mapper)
    : IRequestHandler<UpdateRoleSystemOptionCommand, Response<RoleSystemOptionDto>>
{
    public async Task<Response<RoleSystemOptionDto>> Handle(UpdateRoleSystemOptionCommand request, CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Response<RoleSystemOptionDto>.Error("INVALID_COMPANY_ID", ["CompanyId is required in the request body."]);
        }

        var repository = unitOfWork.RoleSystemOptions;
        var entity = await repository.GetTrackedActiveByIdAndCompanyAsync(request.Id, request.CompanyId, cancellationToken);
        if (entity is null)
        {
            return Response<RoleSystemOptionDto>.Error("ROLE_SYSTEM_OPTION_NOT_FOUND", ["Role system option not found."]);
        }

        mapper.ApplyUpdate(request, entity);
        await repository.UpdateAsync(entity);

        var result = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<RoleSystemOptionDto>.Error("UPDATE_FAILED", ["No records were affected while updating the permission rule."]);
        }

        var readModel = await repository.GetWithNamesAsync(entity.Id, request.CompanyId);
        var dto = readModel is null ? mapper.ToDto(entity) : mapper.ToDto(readModel);

        return new Response<RoleSystemOptionDto>
        {
            IsSuccess = true,
            Message = "Role system option updated successfully.",
            Data = dto
        };
    }
}
