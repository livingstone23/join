namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Input payload for <c>POST /TicketInboundChannels</c>. Lets the operator choose which
/// <c>CommunicationChannel</c> to expose (WhatsApp / SendGrid) and tag the row with a description.
/// </summary>
public record CreateTicketInboundChannelDto
{
    /// <summary> FK to <c>CommunicationChannel</c>. </summary>
    public Guid ChannelId { get; init; }

    /// <summary> Optional description. </summary>
    public string? Description { get; init; }
}
