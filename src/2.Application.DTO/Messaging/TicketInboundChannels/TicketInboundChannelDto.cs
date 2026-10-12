namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Projection of <c>TicketInboundChannel</c> for list endpoints (SPEC 47 F2).
/// The webhook token is **always masked** outside the create response — there is no API path
/// to fetch the clear token after it was issued.
/// </summary>
public record TicketInboundChannelDto
{
    /// <summary> Identifier of the inbound-channel row. </summary>
    public Guid Id { get; init; }

    /// <summary> Tenant the inbound channel belongs to. </summary>
    public Guid CompanyId { get; init; }

    /// <summary> FK to <c>CommunicationChannel</c>. </summary>
    public Guid ChannelId { get; init; }

    /// <summary> Code of the channel (e.g. <c>WHATSAPP</c>, <c>SENDGRID</c>) — drivers routing, never compared by <c>Name</c>. </summary>
    public string ChannelCode { get; init; } = string.Empty;

    /// <summary> Display name of the channel (e.g. <c>WhatsApp</c>, <c>SendGrid</c>). </summary>
    public string ChannelName { get; init; } = string.Empty;

    /// <summary> Operator-supplied description. </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Last 4 characters of the underlying token prefixed by bullets — never the full token.
    /// Format: <c>"••••••••abcd"</c>. <c>null</c> when the masked length is not representable.
    /// </summary>
    public string WebhookTokenMasked { get; init; } = string.Empty;

    /// <summary>
    /// Fully-qualified webhook URL the operator must register on Twilio / SendGrid.
    /// Built by the controller (it knows <c>HttpContext.Request.Scheme/Host</c>), not by the
    /// handler — the URL segment varies by channel (<c>whatsapp</c>/<c>email</c>).
    /// </summary>
    public string WebhookUrl { get; init; } = string.Empty;

    /// <summary> Timestamp UTC. </summary>
    public DateTime CreatedAt { get; init; }
}
