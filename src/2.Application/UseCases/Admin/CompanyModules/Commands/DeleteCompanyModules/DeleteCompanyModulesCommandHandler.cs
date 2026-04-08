using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Commands;

/// <summary>
/// Handles soft delete operations for tenant-scoped company module assignments.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class DeleteCompanyModulesCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteCompanyModulesCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Performs a logical delete by marking the company module assignment as removed.
    /// </summary>
    /// <param name="request">The delete payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the deleted assignment identifier.</returns>
    public async Task<Response<Guid>> Handle(DeleteCompanyModulesCommand request, CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("INVALID_COMPANY_ID", ["The X-Company-Id header is required."]);
        }

        var companyModuleRepository = _unitOfWork.GetRepository<CompanyModule>();
        var existingAssignments = await companyModuleRepository.GetAllAsync();
        var entity = existingAssignments.FirstOrDefault(x =>
            x.Id == request.Id
            && x.CompanyId == request.CompanyId
            && x.GcRecord == 0);

        if (entity is null)
        {
            return Response<Guid>.Error("COMPANY_MODULE_NOT_FOUND", ["Company module not found."]);
        }

        entity.MarkAsDeleted();

        await companyModuleRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the company module assignment."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Company module deleted successfully.",
            Data = entity.Id
        };
    }
}
