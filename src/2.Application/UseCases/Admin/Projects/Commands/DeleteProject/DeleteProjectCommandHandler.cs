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
        var ticketRepository = _unitOfWork.GetRepository<Ticket>();
        var entity = await projectRepository.GetAsync(request.Id);

        if (entity is null || entity.CompanyId != request.CompanyId || entity.GcRecord != 0)
        {
            return Response<Guid>.Error("PROJECT_NOT_FOUND", ["Project not found."]);
        }

        var tickets = await ticketRepository.GetAllAsync();
        var isInUse = tickets.Any(ticket =>
            ticket.CompanyId == request.CompanyId
            && ticket.GcRecord == 0
            && ticket.ProjectId == request.Id);

        if (isInUse)
        {
            return Response<Guid>.Error("PROJECT_IN_USE", ["The project is currently assigned to one or more tickets and cannot be deleted."]);
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