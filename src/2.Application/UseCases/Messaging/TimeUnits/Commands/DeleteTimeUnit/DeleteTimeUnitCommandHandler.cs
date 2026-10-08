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

        var entity = await timeUnitRepository.GetAsync(request.Id);
        if (entity is null)
        {
            return Response<Guid>.Error("TIME_UNIT_NOT_FOUND", ["Time unit not found."]);
        }

        var dependents = new ActiveDependentsCheck(_unitOfWork);
        await dependents.CountAsync<Ticket>(t => t.GcRecord == 0 && t.TimeUnitId == request.Id, "tickets");
        await dependents.CountAsync<TicketComplexity>(tc => tc.GcRecord == 0 && tc.TimeUnitId == request.Id, "ticket complexities");
        await dependents.CountAsync<TicketCompanyDefault>(d => d.GcRecord == 0 && d.TimeUnitDefaultId == request.Id, "ticket company defaults");

        if (dependents.HasDependents)
        {
            return Response<Guid>.Error("TIME_UNIT_IN_USE", dependents.Details);
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
