using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.Mappings.Security.RoleSystemOption;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

/// <summary>
/// Handles updates to role-system-option permission flags.
/// The tenant is always derived from the authenticated caller via <see cref="ICurrentUserService"/>.
/// </summary>
public sealed class UpdateRoleSystemOptionCommandHandler(
    IUnitOfWork unitOfWork,
    IRoleSystemOptionMapper mapper,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateRoleSystemOptionCommand, Response<RoleSystemOptionDto>>
{
    public async Task<Response<RoleSystemOptionDto>> Handle(UpdateRoleSystemOptionCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<RoleSystemOptionDto>.Error("INVALID_COMPANY_ID", ["CompanyId is required in the request body."]);
        }

        if (request.CompanyId.HasValue && request.CompanyId.Value != companyId)
        {
            return Response<RoleSystemOptionDto>.Error(
                "COMPANY_MISMATCH",
                ["CompanyId in the body does not match the authenticated tenant."]);
        }

        var repository = unitOfWork.RoleSystemOptions;
        var entity = await repository.GetTrackedActiveByIdAndCompanyAsync(request.Id, companyId, cancellationToken);
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

        var names = await repository.GetNamesByIdAndCompanyAsync(entity.Id, companyId, cancellationToken);
        var dto = mapper.ToDto(entity);
        if (names is not null)
        {
            dto = dto with
            {
                CompanyName = names.CompanyName,
                RoleName = names.RoleName,
                SystemOptionName = names.SystemOptionName
            };
        }

        return new Response<RoleSystemOptionDto>
        {
            IsSuccess = true,
            Message = "Role system option updated successfully.",
            Data = dto
        };
    }
}