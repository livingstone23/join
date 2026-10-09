using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Projects.Commands;

/// <summary>
/// Handles soft delete operations for tenant-scoped projects.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class DeleteProjectCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteProjectCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Performs a logical delete by marking the project as removed.
    /// </summary>
    /// <param name="request">The delete payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the deleted project identifier.</returns>
    public async Task<Response<Guid>> Handle(DeleteProjectCommand request, CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("INVALID_COMPANY_ID", ["The X-Company-Id header is required."]);
        }

        var projectRepository = _unitOfWork.GetRepository<Project>();
        var entity = await projectRepository.GetAsync(request.Id);

        if (entity is null || entity.CompanyId != request.CompanyId || entity.GcRecord != 0)
        {
            return Response<Guid>.Error("PROJECT_NOT_FOUND", ["Project not found."]);
        }

        var dependents = new ActiveDependentsCheck(_unitOfWork);
        await dependents.CountAsync<Ticket>(t => t.GcRecord == 0 && t.ProjectId == request.Id, "tickets");
        await dependents.CountAsync<TicketCompanyDefault>(d => d.GcRecord == 0 && d.ProjectDefaultId == request.Id, "ticket company defaults");

        if (dependents.HasDependents)
        {
            return Response<Guid>.Error("PROJECT_IN_USE", dependents.Details);
        }

        entity.MarkAsDeleted();

        await projectRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the project."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Project deleted successfully.",
            Data = entity.Id
        };
    }
}