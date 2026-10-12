using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketInboundChannels.Queries.GetTicketInboundChannels;

/// <summary>
/// Query tenant-scoped para listar los <c>TicketInboundChannel</c> activos. El
/// <c>WebhookToken</c> siempre viaja enmascarado fuera del response de create.
/// </summary>
public record GetTicketInboundChannelsQuery(bool? IncludeDeleted = null, Guid? CompanyId = null)
    : IRequest<Response<IReadOnlyCollection<TicketInboundChannelDto>>>;
