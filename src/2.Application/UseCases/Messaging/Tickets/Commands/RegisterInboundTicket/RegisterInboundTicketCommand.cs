using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands.RegisterInboundTicket;

/// <summary>
/// Registers a ticket from a message received on an external channel (WhatsApp, e-mail) — SPEC 47 F4. It is the
/// route without JWT: <c>CompanyId</c> and <c>ChannelId</c> come from the webhook token resolved by the controller,
/// never from <c>ICurrentUserService</c>.
/// <para>
/// Not an <c>ITransactionalCommand</c>: the ticket is committed on its own <c>SaveChangesAsync</c> and the
/// attachments afterwards, best-effort, so a failing attachment never rolls the ticket back; and a lost race on the
/// <c>ExternalMessageId</c> index (PostgreSQL 23505) can be answered with the winner instead of an aborted transaction.
/// </para>
/// </summary>
/// <param name="CompanyId">Tenant resolved from the webhook token.</param>
/// <param name="ChannelId">Channel resolved from the webhook token.</param>
/// <param name="SenderIdentifier">E.164 phone or e-mail address, depending on the channel.</param>
/// <param name="SenderDisplayName">Display name of the sender, when the provider sends it.</param>
/// <param name="MessageSubject">Subject (empty for WhatsApp).</param>
/// <param name="MessageBody">Message text.</param>
/// <param name="ExternalMessageId">Provider message id (Twilio <c>MessageSid</c>, e-mail <c>Message-ID</c> or its SHA-256 fallback): idempotency key.</param>
/// <param name="Attachments">Files received with the message.</param>
/// <param name="SkippedAttachments">
/// Attachments the controller could not obtain (e.g. a failed Twilio media download), with the reason. They are
/// recorded in the internal note of the ticket together with the ones the handler rejects.
/// </param>
public sealed record RegisterInboundTicketCommand(
    Guid CompanyId,
    Guid ChannelId,
    string SenderIdentifier,
    string? SenderDisplayName,
    string MessageSubject,
    string MessageBody,
    string ExternalMessageId,
    IReadOnlyList<InboundAttachment> Attachments,
    IReadOnlyList<string>? SkippedAttachments = null)
    : IRequest<Response<TicketDto>>;
