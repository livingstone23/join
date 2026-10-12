using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.RegisterInboundTicket;

/// <summary>
/// Registra un ticket a partir de un mensaje entrante desde un canal externo
/// (WhatsApp, SendGrid) — SPEC 47 F4. Es la ruta sin JWT: <c>CompanyId</c> y
/// <c>ChannelId</c> se pasan explícitamente porque el token del webhook
/// resolvió el tenant en el controller.
/// <para>
/// <c>ExternalMessageId</c> actúa como clave de idempotencia (Twilio
/// <c>MessageSid</c> / SendGrid <c>Message-ID</c> / hash SHA-256 fallback);
/// el handler la usa para devolver el ticket existente ante un reintento del
/// proveedor, no para crear un duplicado.
/// </para>
/// </summary>
public sealed record RegisterInboundTicketCommand(
    Guid CompanyId,
    Guid ChannelId,
    string SenderIdentifier,
    string? SenderDisplayName,
    string MessageSubject,
    string MessageBody,
    string ExternalMessageId,
    IReadOnlyList<InboundAttachment> Attachments)
    : ITransactionalCommand<Response<TicketDto>>;
