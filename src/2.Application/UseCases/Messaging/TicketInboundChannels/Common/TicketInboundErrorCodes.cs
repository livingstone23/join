namespace JOIN.Application.UseCases.Messaging.TicketInboundChannels.Common;

/// <summary>
/// Error codes returned by SPEC 47 (inbound ticket ingestion) handlers in
/// <c>Response&lt;T&gt;.Message</c>. Co-located with <c>WebhookTokenGenerator</c>
/// because the controllers (F5/F6) and the inbound command (F4) share them.
/// <para>
/// The two codes referenced directly by <c>InboundWhatsAppController.MapRegisterError</c>
/// and <c>InboundEmailController.MapRegisterError</c> — <c>NO_TICKET_ADMIN_CONFIGURED</c>
/// and <c>TICKET_DEFAULTS_NOT_CONFIGURED</c> — are kept as plain string constants
/// without the <c>TICKET_INBOUND_</c> prefix to remain identical to the hard-coded
/// strings the controller branches match against.
/// </para>
/// </summary>
public static class TicketInboundErrorCodes
{
    /// <summary>
    /// The tenant has no <c>TicketUserCompany.IsSuperAdminTicket</c> configured, so the
    /// inbound ticket has no author to attribute the creation to. The provider gets an
    /// explicit 400 — the message is never silently dropped.
    /// </summary>
    public const string NoTicketAdmin = "NO_TICKET_ADMIN_CONFIGURED";

    /// <summary>
    /// The tenant has no <c>TicketCompanyDefault</c> (or its required fields are missing),
    /// so a ticket cannot be created without manual completion.
    /// </summary>
    public const string DefaultsMissing = "TICKET_DEFAULTS_NOT_CONFIGURED";

    /// <summary>
    /// The webhook URL carried a token that doesn't resolve to an active
    /// <c>TicketInboundChannel</c>, or the inbound command's CompanyId/ChannelId
    /// pair doesn't match any active row. The controllers translate this to HTTP 404.
    /// </summary>
    public const string InvalidToken = "TICKET_INBOUND_INVALID_TOKEN";

    /// <summary>
    /// The resolved <c>TicketInboundChannel.ChannelId</c> belongs to a
    /// <c>CommunicationChannel</c> with a <c>Code</c> that the receiving controller
    /// does not handle (e.g. a SENDGRID inbound channel hit by the WhatsApp webhook).
    /// Treated exactly as <see cref="InvalidToken"/> from the outside — HTTP 404.
    /// </summary>
    public const string ChannelMismatch = "TICKET_INBOUND_CHANNEL_MISMATCH";

    /// <summary>
    /// The <c>CommunicationChannel.Code</c> is not one the system supports for inbound
    /// ingestion (webhook on Telegram, Twilio SMS, App, etc.). HTTP 400.
    /// </summary>
    public const string UnsupportedChannel = "TICKET_INBOUND_UNSUPPORTED_CHANNEL";

    /// <summary>
    /// Payload missing required fields (e.g. Twilio posted without <c>MessageSid</c>,
    /// SendGrid without a body). HTTP 400.
    /// </summary>
    public const string PayloadInvalid = "TICKET_INBOUND_PAYLOAD_INVALID";

    /// <summary>
    /// Idempotency hit — a ticket with the same <c>(CompanyId, ChannelId, ExternalMessageId)</c>
    /// already exists. The handler returns it (HTTP 200) with <c>IsDuplicate = true</c>.
    /// This constant is informational and only used in test assertions.
    /// </summary>
    public const string Duplicate = "TICKET_INBOUND_DUPLICATE";

    /// <summary>
    /// Lost the dedup race at insert time (PostgresException 23505). The handler
    /// re-queries and returns the existing ticket with <c>IsDuplicate = true</c>.
    /// </summary>
    public const string DuplicateRace = "TICKET_INBOUND_DUPLICATE_RACE";
}
