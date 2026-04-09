using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TimeUnits.Commands;

/// <summary>
/// Handles soft delete operations for time units.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class DeleteTimeUnitCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteTimeUnitCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Performs a logical delete by marking the time unit as removed.
    /// </summary>
    public async Task<Response<Guid>> Handle(DeleteTimeUnitCommand request, CancellationToken cancellationToken)
    {
        var timeUnitRepository = _unitOfWork.GetRepository<TimeUnit>();
        var ticketRepository = _unitOfWork.GetRepository<Ticket>();
        var ticketComplexityRepository = _unitOfWork.GetRepository<TicketComplexity>();

        var entity = await timeUnitRepository.GetAsync(request.Id);
        if (entity is null)
        {
            return Response<Guid>.Error("TIME_UNIT_NOT_FOUND", ["Time unit not found."]);
        }

        var tickets = await ticketRepository.GetAllAsync();
        var ticketComplexities = await ticketComplexityRepository.GetAllAsync();
        var isInUse = tickets.Any(t => t.GcRecord == 0 && t.TimeUnitId == request.Id)
                   || ticketComplexities.Any(tc => tc.GcRecord == 0 && tc.TimeUnitId == request.Id);

        if (isInUse)
        {
            return Response<Guid>.Error("TIME_UNIT_IN_USE", ["The time unit is currently linked to tickets or ticket complexities and cannot be deleted."]);
        }

        entity.MarkAsDeleted();

        await timeUnitRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the time unit."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Time unit deleted successfully.",
            Data = entity.Id
        };
    }
}
