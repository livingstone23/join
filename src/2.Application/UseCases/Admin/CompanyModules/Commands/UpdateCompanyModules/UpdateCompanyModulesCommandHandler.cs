using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Commands;

/// <summary>
/// Handles company module assignment update commands using the transactional write stack.
/// SPEC 38: only SuperAdmin can update; the target CompanyId is resolved via TenantResolver.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
/// <param name="currentUserService">Resolves the JWT-driven tenant and role context.</param>
public sealed class UpdateCompanyModulesCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : IRequestHandler<UpdateCompanyModulesCommand, Response<CompanyModuleDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentUserService _currentUserService = currentUserService;

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
            return Response<CompanyModuleDto>.Error("INVALID_COMPANY_ID", ["CompanyId is required."]);
        }

        // SPEC 38: tenant scope = explicit request.CompanyId only for SuperAdmin, otherwise the token's tenant.
        var tenantId = TenantResolver.Resolve(_currentUserService, request.CompanyId);

        var companyRepository = _unitOfWork.GetRepository<Company>();
        var moduleRepository = _unitOfWork.GetRepository<SystemModule>();

        var company = await companyRepository.GetAsync(tenantId);
        if (company is null)
        {
            return Response<CompanyModuleDto>.Error("INVALID_COMPANY_ID", ["The specified CompanyId does not exist."]);
        }

        // SPEC 38: Use the named repo (IgnoreQueryFilters + explicit tenant filter) so SuperAdmin
        // can update a CompanyModule for any tenant via TenantResolver.
        var entity = await _unitOfWork.CompanyModules
            .GetByIdForUpdateAsync(request.Id, tenantId, cancellationToken);

        if (entity is null)
        {
            return Response<CompanyModuleDto>.Error("COMPANY_MODULE_NOT_FOUND", ["Company module not found."]);
        }

        entity.IsActive = request.IsActive;

        await _unitOfWork.GetRepository<CompanyModule>().UpdateAsync(entity);
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
