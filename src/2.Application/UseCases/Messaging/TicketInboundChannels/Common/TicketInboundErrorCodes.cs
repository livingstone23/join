namespace JOIN.Application.UseCases.Messaging.TicketInboundChannels.Common;

/// <summary>
/// Error codes returned in <c>Response&lt;T&gt;.Message</c> by the SPEC 47 handlers (inbound channel CRUD and
/// inbound ticket ingestion). The controllers map them to HTTP status codes.
/// </summary>
public static class TicketInboundErrorCodes
{
    /// <summary>
    /// The tenant has no active <c>TicketUserCompany.IsSuperAdminTicket</c>, so the inbound ticket has no author.
    /// HTTP 400 — the provider gets an explicit error and the message is never silently dropped.
    /// </summary>
    public const string NoTicketAdmin = "NO_TICKET_ADMIN_CONFIGURED";

    /// <summary>
    /// The tenant has no <c>TicketCompanyDefault</c>, one of its three required defaults (status, complexity,
    /// time unit) is missing, or the configured initial status is not usable (SPEC 42). HTTP 400.
    /// </summary>
    public const string DefaultsMissing = "TICKET_DEFAULTS_NOT_CONFIGURED";

    /// <summary>
    /// The supplied <c>(CompanyId, ChannelId)</c> has no active <c>TicketInboundChannel</c> (the token was
    /// rotated between the controller lookup and the handler). The webhooks answer a bare HTTP 404.
    /// </summary>
    public const string InboundChannelNotFound = "INBOUND_CHANNEL_NOT_FOUND";

    /// <summary> An active inbound channel already exists for the company and channel. HTTP 409. </summary>
    public const string AlreadyActive = "INBOUND_CHANNEL_ALREADY_ACTIVE";

    /// <summary>
    /// The <c>CommunicationChannel.Code</c> is not one handled by an inbound webhook (only <c>WHATSAPP</c> and
    /// <c>SENDGRID</c> are). HTTP 400.
    /// </summary>
    public const string UnsupportedChannel = "UNSUPPORTED_INBOUND_CHANNEL";

    /// <summary> The communication channel does not exist or is inactive. HTTP 400. </summary>
    public const string InvalidChannel = "INVALID_CHANNEL";

    /// <summary> The inbound channel to delete does not exist in the current tenant. HTTP 404. </summary>
    public const string NotFound = "TICKET_INBOUND_CHANNEL_NOT_FOUND";

    /// <summary>
    /// The provider payload lacks a required field (Twilio without <c>MessageSid</c>, SendGrid without a body).
    /// HTTP 400.
    /// </summary>
    public const string PayloadInvalid = "TICKET_INBOUND_PAYLOAD_INVALID";

    /// <summary>
    /// Informational <c>Message</c> of a successful response when the <c>ExternalMessageId</c> was already
    /// registered and the existing ticket is returned (webhook retry).
    /// </summary>
    public const string Duplicate = "TICKET_INBOUND_DUPLICATE";

    /// <summary> Unique index of active <c>(CompanyId, ChannelId)</c> rows in <c>TicketInboundChannels</c>. </summary>
    public const string CompanyChannelIndex = "UX_TicketInboundChannels_Company_Channel_Active";

    /// <summary> Unique index of active <c>(CompanyId, ChannelId, ExternalMessageId)</c> rows in <c>Tickets</c>. </summary>
    public const string ExternalMessageIdIndex = "UX_Tickets_Company_Channel_ExternalMessageId_Active";
}
