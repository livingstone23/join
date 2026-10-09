using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using JOIN.Domain.Support;
using MediatR;

namespace JOIN.Application.UseCases.Common.CommunicationChannels.Commands;

/// <summary>
/// Handles soft delete operations for communication channels.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public class DeleteCommunicationChannelCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteCommunicationChannelCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Performs a logical delete by marking GcRecord.
    /// </summary>
    public async Task<Response<Guid>> Handle(DeleteCommunicationChannelCommand request, CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.GetRepository<CommunicationChannel>();
        var entity = await repository.GetAsync(request.Id);

        if (entity is null)
        {
            return Response<Guid>.Error("COMMUNICATIONCHANNEL_NOT_FOUND", ["Communication channel not found."]);
        }

        var dependents = new ActiveDependentsCheck(_unitOfWork);
        await dependents.CountAsync<Ticket>(t => t.GcRecord == 0 && t.ChannelId == request.Id, "tickets");
        await dependents.CountAsync<TicketCompanyDefault>(d => d.GcRecord == 0 && d.ChannelDefaultId == request.Id, "ticket company defaults");
        await dependents.CountAsync<TicketNotification>(n => n.GcRecord == 0 && n.CommunicationChannelId == request.Id, "ticket notifications");
        await dependents.CountAsync<UserCommunicationChannel>(u => u.GcRecord == 0 && u.CommunicationChannelId == request.Id, "user communication channels");

        if (dependents.HasDependents)
        {
            return Response<Guid>.Error("COMMUNICATIONCHANNEL_IN_USE", dependents.Details);
        }

        entity.MarkAsDeleted();

        await repository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the communication channel."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Communication channel deleted successfully.",
            Data = entity.Id
        };
    }
}
