using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Commands;

/// <summary>
/// Handles company module assignment creation commands using the transactional write stack.
/// SPEC 38: only SuperAdmin can create; the target CompanyId is resolved via TenantResolver
/// so cross-tenant writes honor an explicit override only when the caller is a real SuperAdmin.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
/// <param name="currentUserService">Resolves the JWT-driven tenant and role context.</param>
public sealed class CreateCompanyModulesCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : IRequestHandler<CreateCompanyModulesCommand, Response<CompanyModuleDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentUserService _currentUserService = currentUserService;

    /// <summary>
    /// Creates a new system module assignment for the specified tenant company.
    /// </summary>
    /// <param name="request">The creation payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response describing the outcome of the create operation.</returns>
    public async Task<Response<CompanyModuleDto>> Handle(CreateCompanyModulesCommand request, CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Response<CompanyModuleDto>.Error("INVALID_COMPANY_ID", ["CompanyId is required."]);
        }

        // SPEC 38: tenant scope = explicit request.CompanyId only for SuperAdmin, otherwise the token's tenant.
        var tenantId = TenantResolver.Resolve(_currentUserService, request.CompanyId);

        var companyRepository = _unitOfWork.GetRepository<Company>();
        var moduleRepository = _unitOfWork.GetRepository<SystemModule>();

        var company = await companyRepository.GetAsync(request.CompanyId);
        if (company is null)
        {
            return Response<CompanyModuleDto>.Error("INVALID_COMPANY_ID", ["The specified CompanyId does not exist."]);
        }

        var module = await moduleRepository.GetAsync(request.ModuleId);
        if (module is null || module.GcRecord != 0)
        {
            return Response<CompanyModuleDto>.Error("SYSTEM_MODULE_NOT_FOUND", ["The specified ModuleId does not exist."]);
        }

        // Use the named repo (IgnoreQueryFilters + explicit tenant filter) so SuperAdmin can
        // create a CompanyModule for any tenant the controller authorized via TenantResolver.
        var assignmentInUse = await _unitOfWork.CompanyModules
            .ExistsActiveAssignmentAsync(tenantId, request.ModuleId, tenantId, cancellationToken);

        if (assignmentInUse)
        {
            return Response<CompanyModuleDto>.Error("COMPANY_MODULE_ALREADY_EXISTS", ["The selected module is already assigned to this company."]);
        }

        var entity = new CompanyModule
        {
            CompanyId = tenantId,
            ModuleId = request.ModuleId,
            IsActive = request.IsActive
        };

        await _unitOfWork.GetRepository<CompanyModule>().InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<CompanyModuleDto>.Error("CREATE_FAILED", ["No records were affected while creating the company module assignment."]);
        }

        return new Response<CompanyModuleDto>
        {
            IsSuccess = true,
            Message = "Company module created successfully.",
            Data = new CompanyModuleDto
            {
                Id = entity.Id,
                CompanyId = entity.CompanyId,
                CompanyName = company.Name,
                ModuleId = entity.ModuleId,
                ModuleName = module.Name,
                IsActive = entity.IsActive,
                CreatedAt = entity.Created
            }
        };
    }
}
