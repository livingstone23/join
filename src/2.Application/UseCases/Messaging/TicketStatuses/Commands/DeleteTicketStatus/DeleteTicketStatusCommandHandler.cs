using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketStatuses.Commands;

/// <summary>
/// Handles soft delete operations for ticket statuses.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class DeleteTicketStatusCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteTicketStatusCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentUserService _currentUserService = currentUserService;

    /// <summary>
    /// Performs a logical delete by marking the ticket status as removed.
    /// </summary>
    public async Task<Response<Guid>> Handle(DeleteTicketStatusCommand request, CancellationToken cancellationToken)
    {
        if (_currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var ticketStatusRepository = _unitOfWork.GetRepository<TicketStatus>();

        var entity = await ticketStatusRepository.GetAsync(request.Id);
        if (entity is null)
        {
            return Response<Guid>.Error("TICKET_STATUS_NOT_FOUND", ["Ticket status not found."]);
        }

        var dependents = new ActiveDependentsCheck(_unitOfWork);
        await dependents.CountAsync<Ticket>(t => t.GcRecord == 0 && t.TicketStatusId == request.Id, "tickets");
        await dependents.CountAsync<TicketCompanyDefault>(d => d.GcRecord == 0 && d.TicketStatusDefaultId == request.Id, "ticket company defaults");
        await dependents.CountAsync<TicketStatusTransition>(
            tr => tr.GcRecord == 0 && (tr.FromStatusId == request.Id || tr.ToStatusId == request.Id),
            "ticket status transitions");

        if (dependents.HasDependents)
        {
            return Response<Guid>.Error("TICKET_STATUS_IN_USE", dependents.Details);
        }

        entity.MarkAsDeleted();

        await ticketStatusRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the ticket status."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Ticket status deleted successfully.",
            Data = entity.Id
        };
    }
}
