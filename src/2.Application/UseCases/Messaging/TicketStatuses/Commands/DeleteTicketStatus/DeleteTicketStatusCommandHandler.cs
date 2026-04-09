using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketStatuses.Commands;

/// <summary>
/// Handles soft delete operations for ticket statuses.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class DeleteTicketStatusCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteTicketStatusCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Performs a logical delete by marking the ticket status as removed.
    /// </summary>
    public async Task<Response<Guid>> Handle(DeleteTicketStatusCommand request, CancellationToken cancellationToken)
    {
        var ticketStatusRepository = _unitOfWork.GetRepository<TicketStatus>();
        var ticketRepository = _unitOfWork.GetRepository<Ticket>();

        var entity = await ticketStatusRepository.GetAsync(request.Id);
        if (entity is null)
        {
            return Response<Guid>.Error("TICKET_STATUS_NOT_FOUND", ["Ticket status not found."]);
        }

        var tickets = await ticketRepository.GetAllAsync();
        var isInUse = tickets.Any(ticket => ticket.GcRecord == 0 && ticket.TicketStatusId == request.Id);

        if (isInUse)
        {
            return Response<Guid>.Error("TICKET_STATUS_IN_USE", ["The ticket status is currently linked to active tickets and cannot be deleted."]);
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
