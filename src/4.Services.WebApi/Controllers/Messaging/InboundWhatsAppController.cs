using Asp.Versioning;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.RegisterInboundTicket;
using JOIN.Domain.Common;
using JOIN.Services.WebApi.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Messaging;

/// <summary>
/// Anonymous webhook for WhatsApp messages received through Twilio (SPEC 47 F5):
/// <c>POST /api/v1/inbound/whatsapp/{token}</c> with Twilio's <c>application/x-www-form-urlencoded</c> body. The
/// token must belong to an active inbound channel whose <c>CommunicationChannel.Code</c> is <c>WHATSAPP</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/inbound/whatsapp")]
[AllowAnonymous]
public class InboundWhatsAppController(
    ISender sender,
    TwilioMediaDownloader mediaDownloader,
    ILogger<InboundWhatsAppController> logger) : InboundTicketWebhookController(sender, logger)
{
    private const string WhatsAppPrefix = "whatsapp:";

    private static readonly Dictionary<string, string> ExtensionsByContentType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = ".pdf",
        ["image/jpeg"] = ".jpg",
        ["image/jpg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/gif"] = ".gif",
        ["text/plain"] = ".txt",
        ["text/csv"] = ".csv",
        ["application/msword"] = ".doc",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
        ["application/vnd.ms-excel"] = ".xls",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ".xlsx",
        ["application/zip"] = ".zip"
    };

    /// <summary>
    /// Registers the WhatsApp message as a ticket and answers <c>{ ticketCode }</c> (the existing code when Twilio
    /// retries the same <c>MessageSid</c>). Unknown, rotated or non-WhatsApp token: bare <c>404</c>.
    /// </summary>
    /// <param name="token">Webhook token of the company's WhatsApp inbound channel.</param>
    /// <param name="form">Twilio fields: <c>From</c>, <c>Body</c>, <c>MessageSid</c>, <c>NumMedia</c>, <c>MediaUrlN</c>, <c>MediaContentTypeN</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("{token}")]
    [Consumes("application/x-www-form-urlencoded")]
    [ProducesResponseType(typeof(InboundTicketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Receive([FromRoute] string token, [FromForm] IFormCollection form, CancellationToken cancellationToken = default)
    {
        var channel = await ResolveChannelAsync(token, CommunicationChannelCodes.WhatsApp, cancellationToken);
        if (channel is null)
        {
            return BareNotFound();
        }

        var messageSid = form["MessageSid"].ToString().Trim();
        var from = StripWhatsAppPrefix(form["From"].ToString());
        if (string.IsNullOrEmpty(messageSid) || string.IsNullOrEmpty(from))
        {
            return InvalidPayload("Twilio payload without MessageSid or From.");
        }

        var attachments = new List<InboundAttachment>();
        var skipped = new List<string>();
        var mediaCount = int.TryParse(form["NumMedia"], out var parsed) ? Math.Max(parsed, 0) : 0;

        for (var i = 0; i < mediaCount; i++)
        {
            var url = form[$"MediaUrl{i}"].ToString();
            var contentType = form[$"MediaContentType{i}"].ToString();
            if (string.IsNullOrWhiteSpace(contentType))
            {
                contentType = "application/octet-stream";
            }

            var fileName = SyntheticFileName(messageSid, i, contentType);
            var download = await mediaDownloader.DownloadAsync(url, cancellationToken);
            if (download.Content is null)
            {
                skipped.Add($"{fileName}: {download.FailureReason}");
                continue;
            }

            attachments.Add(new InboundAttachment(new MemoryStream(download.Content), fileName, contentType, download.Content.LongLength));
        }

        var body = form["Body"].ToString();
        var command = new RegisterInboundTicketCommand(
            CompanyId: channel.CompanyId,
            ChannelId: channel.ChannelId,
            SenderIdentifier: from,
            SenderDisplayName: null,
            MessageSubject: string.Empty,
            MessageBody: body,
            ExternalMessageId: messageSid,
            Attachments: attachments,
            SkippedAttachments: skipped);

        return await RegisterAsync(command, cancellationToken);
    }

    /// <summary> Twilio prefixes <c>From</c> with <c>whatsapp:</c>; the rest of the system uses the bare E.164 number. </summary>
    public static string StripWhatsAppPrefix(string? from)
    {
        var value = from?.Trim() ?? string.Empty;
        return value.StartsWith(WhatsAppPrefix, StringComparison.OrdinalIgnoreCase) ? value[WhatsAppPrefix.Length..].Trim() : value;
    }

    /// <summary>
    /// Twilio sends no file name: the name is built from the message and the media index, with the extension of the
    /// content type so the attachment rules (SPEC 36) can classify it. Unknown types get <c>.bin</c> and are skipped.
    /// </summary>
    public static string SyntheticFileName(string messageSid, int index, string contentType)
    {
        var mediaType = contentType.Split(';', 2)[0].Trim();
        var extension = ExtensionsByContentType.TryGetValue(mediaType, out var known) ? known : ".bin";
        return $"whatsapp-{messageSid}-{index}{extension}";
    }
}
