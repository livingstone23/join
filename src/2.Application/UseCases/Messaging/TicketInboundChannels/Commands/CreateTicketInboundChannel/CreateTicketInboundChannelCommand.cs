using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketInboundChannels.Commands.CreateTicketInboundChannel;

/// <summary>
/// Crea una fila de <c>TicketInboundChannel</c> para el tenant actual, generando el
/// <c>WebhookToken</c> opaco en el handler. El token completo se devuelve una sola vez
/// dentro de <see cref="CreatedTicketInboundChannelDto"/>.
/// </summary>
public record CreateTicketInboundChannelCommand : ITransactionalCommand<Response<CreatedTicketInboundChannelDto>>
{
    /// <summary> FK al <c>CommunicationChannel</c> que el inbound channel escuchará (filtra por <c>Code</c> en el handler). </summary>
    public Guid ChannelId { get; init; }

    /// <summary> Descripción opcional. </summary>
    public string? Description { get; init; }
}
