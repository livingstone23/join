using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Commands;

/// <summary>
/// Handles company module assignment update commands using the transactional write stack.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class UpdateCompanyModulesCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateCompanyModulesCommand, Response<CompanyModuleDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Updates an existing system module assignment for the specified tenant company.
    /// </summary>
    /// <param name="request">The update payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response describing the outcome of the update operation.</returns>
    public async Task<Response<CompanyModuleDto>> Handle(UpdateCompanyModulesCommand request, CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Response<CompanyModuleDto>.Error("INVALID_COMPANY_ID", ["The X-Company-Id header is required."]);
        }

        var companyRepository = _unitOfWork.GetRepository<Company>();
        var companyModuleRepository = _unitOfWork.GetRepository<CompanyModule>();
        var moduleRepository = _unitOfWork.GetRepository<SystemModule>();

        var company = await companyRepository.GetAsync(request.CompanyId);
        if (company is null)
        {
            return Response<CompanyModuleDto>.Error("INVALID_COMPANY_ID", ["The specified CompanyId does not exist."]);
        }

        var existingAssignments = await companyModuleRepository.GetAllAsync();
        var entity = existingAssignments.FirstOrDefault(x =>
            x.Id == request.Id
            && x.CompanyId == request.CompanyId
            && x.GcRecord == 0);

        if (entity is null)
        {
            return Response<CompanyModuleDto>.Error("COMPANY_MODULE_NOT_FOUND", ["Company module not found."]);
        }

        entity.IsActive = request.IsActive;

        await companyModuleRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<CompanyModuleDto>.Error("UPDATE_FAILED", ["No records were affected while updating the company module assignment."]);
        }

        var module = await moduleRepository.GetAsync(entity.ModuleId);

        return new Response<CompanyModuleDto>
        {
            IsSuccess = true,
            Message = "Company module updated successfully.",
            Data = new CompanyModuleDto
            {
                Id = entity.Id,
                CompanyId = entity.CompanyId,
                CompanyName = company.Name,
                ModuleId = entity.ModuleId,
                ModuleName = module?.Name ?? string.Empty,
                IsActive = entity.IsActive,
                CreatedAt = entity.Created
            }
        };
    }
}
