namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Active <c>TicketInboundChannel</c> resolved from an anonymous webhook token (SPEC 47 D): the tenant and the
/// channel the inbound message belongs to. Never leaves the server.
/// </summary>
/// <param name="Id">Identifier of the inbound-channel row.</param>
/// <param name="CompanyId">Tenant that owns the token.</param>
/// <param name="ChannelId">FK to <c>CommunicationChannel</c>.</param>
/// <param name="ChannelCode"><c>CommunicationChannel.Code</c> of the channel.</param>
public sealed record ResolvedTicketInboundChannelDto(Guid Id, Guid CompanyId, Guid ChannelId, string ChannelCode);
