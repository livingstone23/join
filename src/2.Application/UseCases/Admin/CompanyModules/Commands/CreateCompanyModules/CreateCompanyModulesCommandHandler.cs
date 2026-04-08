using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Commands;

/// <summary>
/// Handles company module assignment creation commands using the transactional write stack.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class CreateCompanyModulesCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<CreateCompanyModulesCommand, Response<CompanyModuleDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

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
            return Response<CompanyModuleDto>.Error("INVALID_COMPANY_ID", ["The X-Company-Id header is required."]);
        }

        var companyRepository = _unitOfWork.GetRepository<Company>();
        var moduleRepository = _unitOfWork.GetRepository<SystemModule>();
        var companyModuleRepository = _unitOfWork.GetRepository<CompanyModule>();

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

        var existingAssignments = await companyModuleRepository.GetAllAsync();
        var assignmentInUse = existingAssignments.Any(x =>
            x.CompanyId == request.CompanyId
            && x.ModuleId == request.ModuleId
            && x.GcRecord == 0);

        if (assignmentInUse)
        {
            return Response<CompanyModuleDto>.Error("COMPANY_MODULE_ALREADY_EXISTS", ["The selected module is already assigned to this company."]);
        }

        var entity = new CompanyModule
        {
            CompanyId = request.CompanyId,
            ModuleId = request.ModuleId,
            IsActive = request.IsActive
        };

        await companyModuleRepository.InsertAsync(entity);
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
