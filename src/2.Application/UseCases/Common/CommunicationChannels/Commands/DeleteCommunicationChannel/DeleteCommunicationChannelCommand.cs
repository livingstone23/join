using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.CommunicationChannels.Commands;

/// <summary>
/// Command to perform a soft delete for a communication channel.
/// </summary>
/// <param name="Id">The communication channel identifier to delete.</param>
public record DeleteCommunicationChannelCommand(Guid Id) : IRequest<Response<Guid>>;
