using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Common;
using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Commands.CreateTicketInboundChannel;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Commands.DeleteTicketInboundChannel;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Queries.GetTicketInboundChannels;
using JOIN.Domain.Common;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Messaging;

/// <summary>
/// CRUD administrativo de los <c>TicketInboundChannel</c> del tenant (SPEC 47 F3).
/// Los webhooks anónimos (<c>InboundWhatsAppController</c> / <c>InboundEmailController</c>)
/// NO dependen de este recurso — son siempre libres de JWT — pero el operador
/// necesita alta/baja/listado para configurar qué tokens acepta la empresa.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("TicketInboundChannels")]
public class TicketInboundChannelsController(ISender sender) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    /// <summary>
    /// Lista los canales de ingesta activos del tenant. El <c>WebhookToken</c> siempre
    /// viaja enmascarado; <c>WebhookUrl</c> se devuelve vacío (el navegador reconstruye
    /// la URL a partir de <c>window.location.origin</c> concatenando <c>/api/v1/inbound/{segment}/{token}</c>).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<IReadOnlyCollection<TicketInboundChannelDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool? includeDeleted = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetTicketInboundChannelsQuery(includeDeleted, companyId), cancellationToken);

        if (!response.IsSuccess)
        {
            return MapError(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Crea un nuevo canal de ingesta (un webhook) para el tenant. Devuelve el token
    /// completo <b>una sola vez</b>; posterior lectura solo lo expone enmascarado.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<CreatedTicketInboundChannelDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateTicketInboundChannelDto dto,
        CancellationToken cancellationToken = default)
    {
        var command = new CreateTicketInboundChannelCommand
        {
            ChannelId = dto.ChannelId,
            Description = dto.Description
        };

        var response = await _sender.Send(command, cancellationToken);
        if (!response.IsSuccess)
        {
            return MapError(response);
        }

        var segment = ResolveInboundSegment(response.Data!.ChannelCode);
        if (segment is null)
        {
            // El handler ya validó que el Code sea soportado; esto solo se dispara si el
            // canal sembrado cambió. No exponer el token al cliente en ese caso.
            return BadRequest(Response<object>.Error(TicketInboundErrorCodes.UnsupportedChannel, ["Communication channel is not supported for inbound ingestion."]));
        }

        var built = response.Data with
        {
            WebhookUrl = BuildWebhookUrl(segment, response.Data.WebhookToken)
        };

        return CreatedAtAction(nameof(GetAll), new { id = built.Id }, new Response<CreatedTicketInboundChannelDto>
        {
            IsSuccess = true,
            Message = response.Message,
            Data = built
        });
    }

    /// <summary>
    /// Soft-delete: invalida el token. El siguiente request del proveedor al webhook
    /// con ese token recibe 404.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new DeleteTicketInboundChannelCommand(id), cancellationToken);
        if (!response.IsSuccess)
        {
            return MapError(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Mapea códigos de error del módulo a HTTP status. El listado de canales del
    /// tenant nunca devuelve paginación: una empresa tiene una fila activa por canal
    /// (regla del índice único), así que el tamaño está acotado.
    /// </summary>
    private IActionResult MapError<T>(Response<T> response) => response.Message switch
    {
        "COMPANY_REQUIRED" => Unauthorized(response),
        "TICKET_INBOUND_CHANNEL_NOT_FOUND" => NotFound(response),
        "INBOUND_CHANNEL_ALREADY_ACTIVE" => Conflict(response),
        TicketInboundErrorCodes.UnsupportedChannel => BadRequest(response),
        _ => BadRequest(response)
    };

    /// <summary>
    /// Devuelve el segmento del path que identifica al canal en el webhook, o
    /// <c>null</c> cuando el código no es uno de los canales soportados para ingesta.
    /// El ruteo se decide SIEMPRE por <c>CommunicationChannel.Code</c> y nunca por <c>Name</c>.
    /// </summary>
    private static string? ResolveInboundSegment(string? code) => code switch
    {
        CommunicationChannelCodes.WhatsApp => "whatsapp",
        CommunicationChannelCodes.SendGrid => "email",
        _ => null
    };

    /// <summary>
    /// Construye la URL absoluta del webhook a partir del segmento del canal y el token.
    /// Scheme y host vienen del request actual (<c>X-Forwarded-Proto</c>/<c>X-Forwarded-Host</c>
    /// ya los respeta <see cref="HttpRequest"/>).
    /// </summary>
    private string BuildWebhookUrl(string segment, string token)
    {
        var request = HttpContext.Request;
        return $"{request.Scheme}://{request.Host}/api/v1/inbound/{segment}/{token}";
    }
}
