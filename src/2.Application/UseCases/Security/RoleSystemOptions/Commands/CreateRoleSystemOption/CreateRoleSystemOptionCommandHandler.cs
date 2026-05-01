using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.Mappings.Security.RoleSystemOption;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

/// <summary>
/// Handles the creation of role permission rules for system options.
/// </summary>
public sealed class CreateRoleSystemOptionCommandHandler(
    IUnitOfWork unitOfWork,
    IRoleSystemOptionMapper mapper)
    : IRequestHandler<CreateRoleSystemOptionCommand, Response<RoleSystemOptionDto>>
{
    public async Task<Response<RoleSystemOptionDto>> Handle(CreateRoleSystemOptionCommand request, CancellationToken cancellationToken)
    {
        var companyId = request.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<RoleSystemOptionDto>.Error("INVALID_COMPANY_ID", ["CompanyId is required in the request body."]);
        }

        var companyRepository = unitOfWork.GetRepository<Company>();
        var roleRepository = unitOfWork.GetRepository<ApplicationRole>();
        var optionRepository = unitOfWork.GetRepository<SystemOption>();
        var roleOptionRepository = unitOfWork.RoleSystemOptions;

        var company = await companyRepository.GetAsync(companyId);
        if (company is null || company.GcRecord != 0)
        {
            return Response<RoleSystemOptionDto>.Error("COMPANY_NOT_FOUND", ["Company not found."]);
        }

        var role = await roleRepository.GetAsync(request.RoleId);
        if (role is null || role.GcRecord != 0)
        {
            return Response<RoleSystemOptionDto>.Error("ROLE_NOT_FOUND", ["Role not found."]);
        }

        var option = await optionRepository.GetAsync(request.SystemOptionId);
        if (option is null || option.GcRecord != 0)
        {
            return Response<RoleSystemOptionDto>.Error("SYSTEM_OPTION_NOT_FOUND", ["System option not found."]);
        }

        var exists = await roleOptionRepository.ExistsByRoleAndOptionAsync(companyId, request.RoleId, request.SystemOptionId);
        if (exists)
        {
            return Response<RoleSystemOptionDto>.Error(
                "ROLE_SYSTEM_OPTION_ALREADY_EXISTS",
                ["A permission rule already exists for this role and option in the current company."]);
        }

        var entity = mapper.ToEntity(request);
        await roleOptionRepository.InsertAsync(entity);

        var result = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<RoleSystemOptionDto>.Error("CREATE_FAILED", ["No records were affected while creating the permission rule."]);
        }

        var readModel = await roleOptionRepository.GetWithNamesAsync(entity.Id, companyId);
        var dto = readModel is null ? mapper.ToDto(entity) : mapper.ToDto(readModel);

        return new Response<RoleSystemOptionDto>
        {
            IsSuccess = true,
            Message = "Role system option created successfully.",
            Data = dto
        };
    }
}
