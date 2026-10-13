using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketInboundChannels.Commands.DeleteTicketInboundChannel;

/// <summary>
/// Soft-delete de un <c>TicketInboundChannel</c> (SPEC 47 F2). Invalidar el token es
/// delete + create — un token comprometido no se "recupera" reactivando la fila.
/// </summary>
public record DeleteTicketInboundChannelCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;
