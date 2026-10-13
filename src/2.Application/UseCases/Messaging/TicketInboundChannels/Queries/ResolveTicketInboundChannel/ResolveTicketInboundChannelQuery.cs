using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketInboundChannels.Queries.ResolveTicketInboundChannel;

/// <summary>
/// Resolves the active <c>TicketInboundChannel</c> of an anonymous webhook (SPEC 47 D). Only a token whose channel
/// has <paramref name="ExpectedChannelCode"/> matches, so a SendGrid token pasted in the WhatsApp URL is not found.
/// </summary>
/// <param name="WebhookToken">Token carried in the webhook URL.</param>
/// <param name="ExpectedChannelCode"><c>CommunicationChannel.Code</c> handled by the calling controller.</param>
public sealed record ResolveTicketInboundChannelQuery(string WebhookToken, string ExpectedChannelCode)
    : IRequest<Response<ResolvedTicketInboundChannelDto>>;
