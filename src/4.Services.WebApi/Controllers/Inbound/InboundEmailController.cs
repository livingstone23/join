using System.Security.Cryptography;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Application.UseCases.Messaging.Tickets.RegisterInboundTicket;
using JOIN.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Asp.Versioning;

namespace JOIN.Services.WebApi.Controllers.Inbound;

/// <summary>
/// Webhook anónimo que recibe correos entrantes vía SendGrid Inbound Parse
/// (<c>multipart/form-data</c>) y los traduce a
/// <c>RegisterInboundTicketCommand</c> para crear un ticket sin JWT
/// (SPEC 47 F6).
/// <para>
/// La URL lleva el <c>webhookToken</c> de la empresa/canal configurado en
/// <c>TicketInboundChannel</c>: <c>POST /api/v1/inbound/email/{token}</c>.
/// El token se resuelve contra la BD; sin match, sin canal con
/// <c>Code = SENDGRID</c> activo, o soft-deleted → <c>404</c> desnudo (no
/// se filtra información sobre la validez del token).
/// </para>
/// <para>
/// A diferencia del canal de WhatsApp, los adjuntos llegan ya materializados
/// como partes del multipart (<see cref="IFormFile"/>) — no hay fetch
/// saliente extra.
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/inbound/email")]
[AllowAnonymous]
public class InboundEmailController(
    ISender sender,
    ISqlConnectionFactory connectionFactory,
    ILogger<InboundEmailController> logger) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    private readonly ISqlConnectionFactory _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    private readonly ILogger<InboundEmailController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Recibe un correo entrante desde SendGrid Inbound Parse, lo mapea a un
    /// <see cref="RegisterInboundTicketCommand"/> y devuelve un cuerpo mínimo
    /// con el <c>ticketCode</c> creado (o el existente si el proveedor
    /// reintenta con el mismo <c>Message-ID</c> o hash de payload).
    /// </summary>
    /// <param name="token">Token de webhook que identifica el <c>TicketInboundChannel</c> de la empresa.</param>
    /// <param name="form">Multipart/form-data de SendGrid Inbound Parse.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    [HttpPost("{token}")]
    [Consumes("multipart/form-data")]
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

        var channel = await ResolveChannelAsync(token, CommunicationChannelCodes.SendGrid, cancellationToken);
        if (channel is null)
        {
            return NotFound();
        }

        var from = ParseEmailAddressOnly(form["from"].ToString());
        var senderDisplayName = ParseDisplayName(form["from"].ToString());
        var subject = form["subject"].ToString();
        var text = form["text"].ToString();
        var html = form["html"].ToString();
        var body = !string.IsNullOrEmpty(text) ? text : html;

        if (string.IsNullOrWhiteSpace(body))
        {
            _logger.LogWarning(
                "[InboundEmail] Rejected inbound message: missing both 'text' and 'html' bodies for company {CompanyId}.",
                channel.CompanyId);
            return BadRequest(Response<object>.Error("MISSING_BODY", ["SendGrid did not send a 'text' or 'html' body."]));
        }

        var attachments = new List<InboundAttachment>();
        var attachmentCount = int.TryParse(form["attachments"], out var n) ? n : 0;
        for (var i = 1; i <= attachmentCount; i++)
        {
            var file = form.Files.GetFile($"attachment{i}");
            if (file is null || file.Length == 0)
            {
                continue;
            }

            // Copia a MemoryStream para que el handler pueda leerlo sin
            // depender del ciclo de vida del IFormFile (que ASP.NET dispone
            // al terminar la request). El buffer NO se envuelve en `using`
            // porque su dueño pasa a ser el InboundAttachment: si lo
            // disposáramos acá, el handler recibiría un stream ya cerrado.
            var sourceStream = file.OpenReadStream();
            var buffer = new MemoryStream(checked((int)file.Length));
            await sourceStream.CopyToAsync(buffer, cancellationToken);
            sourceStream.Dispose();
            buffer.Position = 0;

            attachments.Add(new InboundAttachment(
                buffer,
                file.FileName,
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                file.Length));
        }

        var externalMessageId = ExtractMessageId(form["headers"].ToString())
            ?? ComputePayloadHash(from, subject, body);

        var command = new RegisterInboundTicketCommand(
            CompanyId: channel.CompanyId,
            ChannelId: channel.ChannelId,
            SenderIdentifier: from,
            SenderDisplayName: senderDisplayName,
            MessageSubject: subject ?? string.Empty,
            MessageBody: body,
            ExternalMessageId: externalMessageId,
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
    /// verificando además que su <c>CommunicationChannel.Code</c> sea
    /// <c>SENDGRID</c>. Devuelve <c>null</c> en cualquier caso de "no match".
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

    /// <summary>
    /// De un From tipo <c>"Name" &lt;a@b.com&gt;</c> deja solo <c>a@b.com</c>.
    /// </summary>
    private static string ParseEmailAddressOnly(string rawFrom)
    {
        if (string.IsNullOrWhiteSpace(rawFrom))
        {
            return string.Empty;
        }

        var start = rawFrom.IndexOf('<');
        var end = rawFrom.IndexOf('>');
        if (start >= 0 && end > start)
        {
            return rawFrom.Substring(start + 1, end - start - 1).Trim();
        }

        return rawFrom.Trim();
    }

    /// <summary>
    /// De un From tipo <c>"Name" &lt;a@b.com&gt;</c> extrae <c>Name</c>. Si no
    /// viene en ese formato, devuelve <c>null</c>.
    /// </summary>
    private static string? ParseDisplayName(string rawFrom)
    {
        if (string.IsNullOrWhiteSpace(rawFrom))
        {
            return null;
        }

        var lt = rawFrom.IndexOf('<');
        if (lt <= 0)
        {
            return null;
        }

        var display = rawFrom[..lt].Trim().Trim('"', '\'');
        return string.IsNullOrWhiteSpace(display) ? null : display;
    }

    /// <summary>
    /// Busca una línea <c>Message-ID: &lt;id&gt;</c> en el bloque de headers
    /// crudo que SendGrid entrega en el campo <c>headers</c> del multipart.
    /// Devuelve <c>null</c> si no la encuentra — en ese caso el caller usa
    /// un hash del payload como fallback determinístico.
    /// </summary>
    private static string? ExtractMessageId(string? rawHeaders)
    {
        if (string.IsNullOrWhiteSpace(rawHeaders))
        {
            return null;
        }

        foreach (var rawLine in rawHeaders.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            const string prefix = "Message-ID:";
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = line[prefix.Length..].Trim();
                if (value.Length >= 2 && value[0] == '<' && value[^1] == '>')
                {
                    value = value[1..^1];
                }
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }

    /// <summary>
    /// Hash SHA-256 determinístico de los campos que identifican unívocamente
    /// al mensaje cuando SendGrid no expone <c>Message-ID</c>: misma
    /// combinación <c>(from, subject, body)</c> en reintentos del proveedor
    /// produce siempre el mismo id, habilitando la deduplicación.
    /// </summary>
    private static string ComputePayloadHash(string from, string subject, string body)
    {
        var payload = $"{from}\n{subject}\n{body}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record ResolvedChannel(Guid Id, Guid CompanyId, Guid ChannelId);
}
