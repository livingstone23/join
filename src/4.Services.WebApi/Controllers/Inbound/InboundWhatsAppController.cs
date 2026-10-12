using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Application.UseCases.Messaging.Tickets.RegisterInboundTicket;
using JOIN.Domain.Common;
using JOIN.Services.WebApi.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Asp.Versioning;

namespace JOIN.Services.WebApi.Controllers.Inbound;

/// <summary>
/// Webhook anónimo que recibe mensajes de WhatsApp vía Twilio
/// (<c>application/x-www-form-urlencoded</c>) y los traduce a
/// <c>RegisterInboundTicketCommand</c> para crear un ticket sin JWT
/// (SPEC 47 F5).
/// <para>
/// La URL lleva el <c>webhookToken</c> de la empresa/canal configurado en
/// <c>TicketInboundChannel</c>: <c>POST /api/v1/inbound/whatsapp/{token}</c>.
/// El token se resuelve contra la BD; sin match, sin canal con
/// <c>Code = WHATSAPP</c> activo, o soft-deleted → <c>404</c> desnudo (no se
/// filtra información sobre la validez del token).
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/inbound/whatsapp")]
[AllowAnonymous]
public class InboundWhatsAppController(
    ISender sender,
    ISqlConnectionFactory connectionFactory,
    IConfiguration configuration,
    ILogger<InboundWhatsAppController> logger) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    private readonly ISqlConnectionFactory _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly ILogger<InboundWhatsAppController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Recibe un mensaje de WhatsApp entrante desde Twilio, lo mapea a un
    /// <see cref="RegisterInboundTicketCommand"/> y devuelve un cuerpo mínimo
    /// con el <c>ticketCode</c> creado (o el existente si el proveedor
    /// reintenta con el mismo <c>MessageSid</c>).
    /// </summary>
    /// <param name="token">Token de webhook que identifica el <c>TicketInboundChannel</c> de la empresa.</param>
    /// <param name="form">Form-urlencoded body de Twilio (<c>From</c>, <c>Body</c>, <c>MessageSid</c>, <c>NumMedia</c>, <c>MediaUrlN</c>, <c>MediaContentTypeN</c>).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    [HttpPost("{token}")]
    [Consumes("application/x-www-form-urlencoded")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Receive(
        [FromRoute] string token,
        [FromForm] IFormCollection form,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return NotFound();
        }

        var channel = await ResolveChannelAsync(token, CommunicationChannelCodes.WhatsApp, cancellationToken);
        if (channel is null)
        {
            return NotFound();
        }

        var fromRaw = form["From"].ToString();
        var body = form["Body"].ToString();
        var messageSid = form["MessageSid"].ToString();

        if (string.IsNullOrWhiteSpace(messageSid))
        {
            _logger.LogWarning(
                "[InboundWhatsApp] Rejected inbound message: missing MessageSid for company {CompanyId}.",
                channel.CompanyId);
            return BadRequest(Response<object>.Error("MISSING_MESSAGE_SID", ["Twilio did not send a MessageSid."]));
        }

        // Twilio antepone "whatsapp:" al From; el resto del sistema trabaja con el E.164 puro.
        var from = StripWhatsAppPrefix(fromRaw);

        var numMedia = int.TryParse(form["NumMedia"], out var n) ? n : 0;
        var attachments = new List<InboundAttachment>();
        for (var i = 0; i < numMedia; i++)
        {
            var url = form[$"MediaUrl{i}"].ToString();
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var contentType = form[$"MediaContentType{i}"].ToString();
            if (string.IsNullOrWhiteSpace(contentType))
            {
                contentType = "application/octet-stream";
            }

            var bytes = await TwilioMediaDownloader.TryDownloadAsync(url, _configuration, _logger, cancellationToken);
            if (bytes is null)
            {
                // Best-effort: la spec dice que un fallo de descarga NO aborta el alta del ticket.
                continue;
            }

            var fileName = ExtractFileNameFromUrl(url);
            attachments.Add(new InboundAttachment(
                new MemoryStream(bytes),
                fileName,
                contentType,
                bytes.LongLength));
        }

        var command = new RegisterInboundTicketCommand(
            CompanyId: channel.CompanyId,
            ChannelId: channel.ChannelId,
            SenderIdentifier: from,
            SenderDisplayName: null,
            MessageSubject: body ?? string.Empty,
            MessageBody: body ?? string.Empty,
            ExternalMessageId: messageSid,
            Attachments: attachments);

        var response = await _sender.Send(command, cancellationToken);
        if (!response.IsSuccess)
        {
            return MapRegisterError(response);
        }

        return Ok(new InboundTicketResponse(
            TicketId: response.Data!.Id,
            TicketCode: response.Data.Code,
            IsDuplicate: false));
    }

    /// <summary>
    /// Resuelve un <c>TicketInboundChannel</c> activo por su token de webhook,
    /// verificando además que su <c>CommunicationChannel.Code</c> sea el
    /// esperado por este controller. Devuelve <c>null</c> en cualquier caso
    /// de "no match" — el controller responderá <c>404</c> desnudo sin
    /// distinguir entre "token inexistente", "soft-deleted" o "Code incorrecto".
    /// </summary>
    private async Task<ResolvedChannel?> ResolveChannelAsync(string token, string expectedCode, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT tic.Id, tic.CompanyId, tic.ChannelId
            FROM Messaging.TicketInboundChannels tic
            INNER JOIN Common.CommunicationChannels cc ON cc.Id = tic.ChannelId
            WHERE tic.WebhookToken = @token
              AND tic.GcRecord = 0
              AND cc.GcRecord = 0
              AND cc.Code = @expectedCode
            LIMIT 1
            """;

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<ResolvedChannel>(
            new CommandDefinition(
                sql,
                new { token, expectedCode },
                cancellationToken: cancellationToken));

        return row;
    }

    private IActionResult MapRegisterError(Response<TicketDto> response)
    {
        return response.Message switch
        {
            "NO_TICKET_ADMIN_CONFIGURED" or "TICKET_DEFAULTS_NOT_CONFIGURED"
                => BadRequest(response),
            _ => BadRequest(response)
        };
    }

    private static string StripWhatsAppPrefix(string from)
    {
        if (string.IsNullOrEmpty(from))
        {
            return string.Empty;
        }

        const string prefix = "whatsapp:";
        return from.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? from[prefix.Length..]
            : from;
    }

    private static string ExtractFileNameFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var lastSegment = uri.Segments.Length > 0 ? uri.Segments[^1] : null;
            return string.IsNullOrWhiteSpace(lastSegment) ? "media" : Uri.UnescapeDataString(lastSegment);
        }
        catch
        {
            return "media";
        }
    }

    private sealed record ResolvedChannel(Guid Id, Guid CompanyId, Guid ChannelId);
}
