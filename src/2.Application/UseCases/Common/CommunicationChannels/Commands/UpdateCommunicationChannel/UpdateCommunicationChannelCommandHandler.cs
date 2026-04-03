using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.CommunicationChannels.Commands;

/// <summary>
/// Handles communication channel update commands.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public class UpdateCommunicationChannelCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateCommunicationChannelCommand, Response<CommunicationChannelDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Updates an existing communication channel.
    /// </summary>
    public async Task<Response<CommunicationChannelDto>> Handle(UpdateCommunicationChannelCommand request, CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.GetRepository<CommunicationChannel>();
        var entity = await repository.GetAsync(request.Id);

        if (entity is null)
        {
            return Response<CommunicationChannelDto>.Error("COMMUNICATIONCHANNEL_NOT_FOUND", ["Communication channel not found."]);
        }

        var normalizedName = request.Name.Trim();

        var channels = await repository.GetAllAsync();
        var nameInUse = channels.Any(c => c.Id != request.Id && string.Equals(c.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        if (nameInUse)
        {
            return Response<CommunicationChannelDto>.Error("COMMUNICATIONCHANNEL_NAME_IN_USE", ["Another active communication channel already uses the same name."]);
        }

        entity.Name = normalizedName;
        entity.Provider = request.Provider?.Trim();
        entity.Code = request.Code?.Trim();
        entity.IsActive = request.IsActive;

        await repository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<CommunicationChannelDto>.Error("UPDATE_FAILED", ["No records were affected while updating the communication channel."]);
        }

        return new Response<CommunicationChannelDto>
        {
            IsSuccess = true,
            Message = "Communication channel updated successfully.",
            Data = new CommunicationChannelDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Provider = entity.Provider,
                Code = entity.Code,
                IsActive = entity.IsActive
            }
        };
    }
}
