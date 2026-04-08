using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.EntityStatuses.Commands;

/// <summary>
/// Handles soft delete operations for administrative entity statuses.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class DeleteEntityStatusCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteEntityStatusCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Performs a logical delete by marking the entity status as removed.
    /// </summary>
    /// <param name="request">The delete payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the deleted entity status identifier.</returns>
    public async Task<Response<Guid>> Handle(DeleteEntityStatusCommand request, CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("INVALID_COMPANY_ID", ["The X-Company-Id header is required."]);
        }

        var companyRepository = _unitOfWork.GetRepository<Company>();
        var statusRepository = _unitOfWork.GetRepository<EntityStatus>();
        var areaRepository = _unitOfWork.GetRepository<Area>();
        var projectRepository = _unitOfWork.GetRepository<Project>();

        var company = await companyRepository.GetAsync(request.CompanyId);
        if (company is null)
        {
            return Response<Guid>.Error("INVALID_COMPANY_ID", ["The specified CompanyId does not exist."]);
        }

        var entity = await statusRepository.GetAsync(request.Id);
        if (entity is null || entity.GcRecord != 0)
        {
            return Response<Guid>.Error("ENTITY_STATUS_NOT_FOUND", ["Entity status not found."]);
        }

        var areas = await areaRepository.GetAllAsync();
        var projects = await projectRepository.GetAllAsync();
        var isInUse = areas.Any(area => area.GcRecord == 0 && area.EntityStatusId == request.Id)
                    || projects.Any(project => project.GcRecord == 0 && project.EntityStatusId == request.Id);

        if (isInUse)
        {
            return Response<Guid>.Error("ENTITY_STATUS_IN_USE", ["The entity status is currently assigned to areas or projects and cannot be deleted."]);
        }

        entity.MarkAsDeleted();

        await statusRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the entity status."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Entity status deleted successfully.",
            Data = entity.Id
        };
    }
}
