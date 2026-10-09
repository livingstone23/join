using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketComplexities.Commands;

/// <summary>
/// Handles soft delete operations for ticket complexities.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class DeleteTicketComplexityCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteTicketComplexityCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Performs a logical delete by marking the ticket complexity as removed.
    /// </summary>
    public async Task<Response<Guid>> Handle(DeleteTicketComplexityCommand request, CancellationToken cancellationToken)
    {
        var ticketComplexityRepository = _unitOfWork.GetRepository<TicketComplexity>();

        var entity = await ticketComplexityRepository.GetAsync(request.Id);
        if (entity is null)
        {
            return Response<Guid>.Error("TICKET_COMPLEXITY_NOT_FOUND", ["Ticket complexity not found."]);
        }

        var dependents = new ActiveDependentsCheck(_unitOfWork);
        await dependents.CountAsync<Ticket>(t => t.GcRecord == 0 && t.TicketComplexityId == request.Id, "tickets");
        await dependents.CountAsync<TicketCompanyDefault>(d => d.GcRecord == 0 && d.TicketComplexityDefaultId == request.Id, "ticket company defaults");

        if (dependents.HasDependents)
        {
            return Response<Guid>.Error("TICKET_COMPLEXITY_IN_USE", dependents.Details);
        }

        entity.MarkAsDeleted();

        await ticketComplexityRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the ticket complexity."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Ticket complexity deleted successfully.",
            Data = entity.Id
        };
    }
}
