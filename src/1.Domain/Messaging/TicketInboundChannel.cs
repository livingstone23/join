using JOIN.Domain.Audit;
using JOIN.Domain.Common;

namespace JOIN.Domain.Messaging;

/// <summary>
/// Configures one inbound webhook endpoint per tenant (SPEC 47).
/// Each row binds a <see cref="CommunicationChannel"/> (e.g. WhatsApp, SendGrid) to a tenant and
/// exposes the <see cref="WebhookToken"/> that anonymous webhook URLs resolve to. The token is the
/// only credential needed — no JWT, no <c>X-Company-Id</c> header.
/// <para>
/// Active vs. inactive state is governed exclusively by the soft-delete
/// <see cref="BaseAuditableEntity.GcRecord"/> column — there is no separate <c>IsActive</c> flag.
/// Soft-deleting the row invalidates the webhook token immediately (a subsequent request to its URL
/// resolves to 404). Rotating a token is therefore a soft-delete + create, not an in-place update.
/// </para>
/// </summary>
public class TicketInboundChannel : BaseTenantEntity
{
    /// <summary>
    /// Foreign key to the <see cref="CommunicationChannel"/> this inbound channel listens on
    /// (e.g. the row with <c>Code = WHATSAPP</c> or <c>Code = SENDGRID</c>).
    /// </summary>
    public Guid ChannelId { get; set; }

    /// <summary>
    /// Opaque, server-generated secret appearing in the webhook URL (e.g. <c>/api/v1/inbound/whatsapp/{token}</c>).
    /// At least 32 bytes of entropy encoded as Base64Url (43+ characters) — no padding, no Guid-like look.
    /// Unique globally: the lookup is anonymous by token, so the same token cannot exist in two tenants.
    /// </summary>
    public string WebhookToken { get; set; } = string.Empty;

    /// <summary>
    /// Optional human-readable description of what this endpoint is for (e.g. "Soporte WhatsApp principal").
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Navigation to the <see cref="CommunicationChannel"/> this row belongs to.
    /// </summary>
    public virtual CommunicationChannel Channel { get; set; } = null!;
}
