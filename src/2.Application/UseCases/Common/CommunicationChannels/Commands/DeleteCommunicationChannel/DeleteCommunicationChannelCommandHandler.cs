using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
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

        entity.GcRecord = 1;

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
