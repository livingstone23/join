using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.Mappings.Security.RoleSystemOption;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

/// <summary>
/// Handles the creation of role permission rules for system options.
/// </summary>
public sealed class CreateRoleSystemOptionCommandHandler(
    IUnitOfWork unitOfWork,
    IRoleSystemOptionMapper mapper,
    IAuditLogger auditLogger)
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

        // EF-only readback to populate the three display names (Role/SystemOption/Company).
        // Must run on the same DbContext/connection as the INSERT to avoid the cross-connection
        // X-lock timeout (SQL Server default isolation, no RCSI here) — Dapper's GetWithNamesAsync
        // would block for 30s against the row the outer TransactionBehavior still holds.
        var names = await roleOptionRepository.GetNamesByIdAndCompanyAsync(entity.Id, companyId, cancellationToken);
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

        await auditLogger.LogAsync(
            AuditedEntity.RoleSystemOption,
            entity.Id,
            AuditAction.Created,
            entityLabel: names is not null ? $"{names.RoleName} → {names.SystemOptionName}" : null,
            newValues: new Dictionary<string, object?>
            {
                ["CanRead"] = entity.CanRead,
                ["CanCreate"] = entity.CanCreate,
                ["CanUpdate"] = entity.CanUpdate,
                ["CanDelete"] = entity.CanDelete,
                ["CanDownload"] = entity.CanDownload,
                ["CanExport"] = entity.CanExport,
                ["CanExecute"] = entity.CanExecute,
                ["IsVisibleMenu"] = entity.IsVisibleMenu,
                ["OrderMenu"] = entity.OrderMenu
            },
            ct: cancellationToken);

        return new Response<RoleSystemOptionDto>
        {
            IsSuccess = true,
            Message = "Role system option created successfully.",
            Data = dto
        };
    }
}
