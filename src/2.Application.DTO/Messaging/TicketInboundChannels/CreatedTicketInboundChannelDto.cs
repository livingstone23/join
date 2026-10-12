namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Output of <c>POST /TicketInboundChannels</c>. The <b>only</b> moment the clear
/// <see cref="WebhookToken"/> travels in a response — same shape as an API key issued
/// by any third-party provider.
/// </summary>
public record CreatedTicketInboundChannelDto
{
    /// <summary> Identifier of the new row. </summary>
    public Guid Id { get; init; }

    /// <summary> Tenant. </summary>
    public Guid CompanyId { get; init; }

    /// <summary> Channel FK. </summary>
    public Guid ChannelId { get; init; }

    /// <summary> <c>CommunicationChannel.Code</c> echoed for the UI. </summary>
    public string ChannelCode { get; init; } = string.Empty;

    /// <summary> Display name of the channel. </summary>
    public string ChannelName { get; init; } = string.Empty;

    /// <summary> Operator-supplied description. </summary>
    public string? Description { get; init; }

    /// <summary>The clear token the operator must configure as the URL secret on Twilio / SendGrid.</summary>
    public string WebhookToken { get; init; } = string.Empty;

    /// <summary>The webhook URL the operator must register with the provider.</summary>
    public string WebhookUrl { get; init; } = string.Empty;

    /// <summary> Timestamp UTC. </summary>
    public DateTime CreatedAt { get; init; }
}
