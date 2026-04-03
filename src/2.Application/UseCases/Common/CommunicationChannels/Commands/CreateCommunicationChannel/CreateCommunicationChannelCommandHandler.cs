using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.CommunicationChannels.Commands;

/// <summary>
/// Handles communication channel creation commands.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public class CreateCommunicationChannelCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<CreateCommunicationChannelCommand, Response<CommunicationChannelDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Creates a communication channel.
    /// </summary>
    public async Task<Response<CommunicationChannelDto>> Handle(CreateCommunicationChannelCommand request, CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.GetRepository<CommunicationChannel>();
        var normalizedName = request.Name.Trim();

        var channels = await repository.GetAllAsync();
        var nameInUse = channels.Any(c => string.Equals(c.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        if (nameInUse)
        {
            return Response<CommunicationChannelDto>.Error("COMMUNICATIONCHANNEL_NAME_IN_USE", ["Another active communication channel already uses the same name."]);
        }

        var entity = new CommunicationChannel
        {
            Name = normalizedName,
            Provider = request.Provider?.Trim(),
            Code = request.Code?.Trim(),
            IsActive = request.IsActive
        };

        await repository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<CommunicationChannelDto>.Error("CREATE_FAILED", ["No records were affected while creating the communication channel."]);
        }

        return new Response<CommunicationChannelDto>
        {
            IsSuccess = true,
            Message = "Communication channel created successfully.",
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
