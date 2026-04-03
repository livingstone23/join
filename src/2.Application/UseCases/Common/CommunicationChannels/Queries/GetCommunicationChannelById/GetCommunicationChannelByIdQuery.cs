using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.CommunicationChannels.Queries;

/// <summary>
/// Query to retrieve a communication channel by id.
/// </summary>
/// <param name="CommunicationChannelId">The communication channel identifier.</param>
public record GetCommunicationChannelByIdQuery(Guid CommunicationChannelId) : IRequest<Response<CommunicationChannelDto>>;
