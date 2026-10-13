using System.Security.Cryptography;
using System.Text;
using Asp.Versioning;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.RegisterInboundTicket;
using JOIN.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Messaging;

/// <summary>
/// Anonymous webhook for e-mails received through SendGrid Inbound Parse (SPEC 47 F6):
/// <c>POST /api/v1/inbound/email/{token}</c> with SendGrid's <c>multipart/form-data</c> body. The token must belong
/// to an active inbound channel whose <c>CommunicationChannel.Code</c> is <c>SENDGRID</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/inbound/email")]
[AllowAnonymous]
public class InboundEmailController(
    ISender sender,
    ILogger<InboundEmailController> logger) : InboundTicketWebhookController(sender, logger)
{
    /// <summary>
    /// Registers the e-mail as a ticket and answers <c>{ ticketCode }</c> (the existing code when SendGrid retries
    /// the same message). Unknown, rotated or non-SendGrid token: bare <c>404</c>.
    /// </summary>
    /// <param name="token">Webhook token of the company's e-mail inbound channel.</param>
    /// <param name="form">SendGrid fields: <c>from</c>, <c>subject</c>, <c>text</c>/<c>html</c>, <c>headers</c>, <c>attachments</c> and the files <c>attachmentN</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("{token}")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(InboundTicketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Receive([FromRoute] string token, [FromForm] IFormCollection form, CancellationToken cancellationToken = default)
    {
        var channel = await ResolveChannelAsync(token, CommunicationChannelCodes.SendGrid, cancellationToken);
        if (channel is null)
        {
            return BareNotFound();
        }

        var rawFrom = form["from"].ToString();
        var from = ParseAddress(rawFrom);
        var subject = form["subject"].ToString().Trim();
        var text = form["text"].ToString();
        var body = string.IsNullOrWhiteSpace(text) ? form["html"].ToString() : text;

        if (string.IsNullOrEmpty(from) || string.IsNullOrWhiteSpace(body))
        {
            return InvalidPayload("SendGrid payload without 'from' or without a 'text'/'html' body.");
        }

        var attachments = new List<InboundAttachment>();
        foreach (var file in form.Files.Where(f => f.Length > 0))
        {
            var buffer = new MemoryStream();
            await using (var source = file.OpenReadStream())
            {
                await source.CopyToAsync(buffer, cancellationToken);
            }

            buffer.Position = 0;
            attachments.Add(new InboundAttachment(
                buffer,
                string.IsNullOrWhiteSpace(file.FileName) ? file.Name : file.FileName,
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                file.Length));
        }

        var command = new RegisterInboundTicketCommand(
            CompanyId: channel.CompanyId,
            ChannelId: channel.ChannelId,
            SenderIdentifier: from,
            SenderDisplayName: ParseDisplayName(rawFrom),
            MessageSubject: subject,
            MessageBody: body,
            ExternalMessageId: ExtractMessageId(form["headers"].ToString()) ?? ComputePayloadHash(from, subject, body),
            Attachments: attachments);

        return await RegisterAsync(command, cancellationToken);
    }

    /// <summary> Address of a <c>From</c> such as <c>"Name &lt;a@b.com&gt;"</c>, or the trimmed value. </summary>
    public static string ParseAddress(string? rawFrom)
    {
        var value = rawFrom?.Trim() ?? string.Empty;
        var start = value.LastIndexOf('<');
        var end = value.LastIndexOf('>');
        return start >= 0 && end > start ? value[(start + 1)..end].Trim() : value;
    }

    /// <summary> Display name of a <c>From</c> such as <c>"Name &lt;a@b.com&gt;"</c>; <c>null</c> when absent. </summary>
    public static string? ParseDisplayName(string? rawFrom)
    {
        var value = rawFrom?.Trim() ?? string.Empty;
        var start = value.LastIndexOf('<');
        if (start <= 0)
        {
            return null;
        }

        var display = value[..start].Trim().Trim('"', '\'').Trim();
        return display.Length == 0 ? null : display;
    }

    /// <summary> <c>Message-ID</c> of the raw <c>headers</c> field, without angle brackets; <c>null</c> when absent. </summary>
    public static string? ExtractMessageId(string? rawHeaders)
    {
        const string prefix = "Message-ID:";
        foreach (var rawLine in (rawHeaders ?? string.Empty).Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[prefix.Length..].Trim().TrimStart('<').TrimEnd('>').Trim();
            return value.Length == 0 ? null : value;
        }

        return null;
    }

    /// <summary>
    /// Deterministic fallback key when SendGrid does not expose <c>Message-ID</c>: SHA-256 of from + subject + body
    /// (SPEC 47 "Decisions"), so a retry of the same e-mail is recognized.
    /// </summary>
    public static string ComputePayloadHash(string from, string subject, string body) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{from}\n{subject}\n{body}"))).ToLowerInvariant();
}
