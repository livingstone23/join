using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Commands;
using JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace JOIN.Services.WebApi.Controllers.Messaging;

/// <summary>
/// Exposes REST endpoints for tenant ticket attachment settings management.
/// La configuración es singleton por empresa (0 o 1 fila activa), así que el
/// listado tenant-scoped no se pagina — el paginado cross-tenant es solo
/// para usuarios <c>SuperAdmin</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("TicketAttachmentSettings")]
public class TicketAttachmentSettingsController(ISender sender) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    /// <summary>
    /// Recupera la fila activa de la empresa actual (0 o 1 elemento).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<IReadOnlyCollection<TicketAttachmentSettingsDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool? includeDeleted = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetTicketAttachmentSettingsQuery(includeDeleted, companyId), cancellationToken);

        if (!response.IsSuccess)
        {
            return MapError(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Recupera la lista paginada cross-tenant. Acceso restringido a SuperAdmin.
    /// </summary>
    [HttpGet("system-wide")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(Response<PagedResult<TicketAttachmentSettingsDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSystemWide(
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? companyName = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(
            new GetSystemWideTicketAttachmentSettingsQuery(pageNumber, pageSize, companyName),
            cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Recupera una configuración por identificador.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<TicketAttachmentSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromQuery] bool? includeDeleted = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetTicketAttachmentSettingByIdQuery(id, includeDeleted, companyId), cancellationToken);

        if (!response.IsSuccess)
        {
            return MapError(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Crea la configuración de adjuntos del tenant. Una sola fila activa —
    /// un segundo <c>POST</c> devuelve 409 CONFIG_ALREADY_EXISTS.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<TicketAttachmentSettingsDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateTicketAttachmentSettingsDto dto, CancellationToken cancellationToken = default)
    {
        var command = new CreateTicketAttachmentSettingsCommand
        {
            AllowedDocumentTypes = dto.AllowedDocumentTypes,
            MaxFileSizeBytes = dto.MaxFileSizeBytes,
            MaxFilesPerTicket = dto.MaxFilesPerTicket,
            MaxFilesPerDay = dto.MaxFilesPerDay
        };

        var response = await _sender.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return MapError(response);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.Data!.Id }, response);
    }

    /// <summary>
    /// Actualiza la configuración de adjuntos del tenant.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<TicketAttachmentSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTicketAttachmentSettingsDto dto, CancellationToken cancellationToken = default)
    {
        var command = new UpdateTicketAttachmentSettingsCommand
        {
            Id = id,
            AllowedDocumentTypes = dto.AllowedDocumentTypes,
            MaxFileSizeBytes = dto.MaxFileSizeBytes,
            MaxFilesPerTicket = dto.MaxFilesPerTicket,
            MaxFilesPerDay = dto.MaxFilesPerDay
        };

        var response = await _sender.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return MapError(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Borrado lógico de la configuración de adjuntos.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new DeleteTicketAttachmentSettingsCommand(id), cancellationToken);

        if (!response.IsSuccess)
        {
            return MapError(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Mapea códigos de negocio a HTTP para los endpoints tenant-scoped.
    /// Coherente con el resto del módulo — `COMPANY_REQUIRED` siempre 401
    /// (no 400 por el fallback de <c>BadRequest</c>), `TICKET_ATTACHMENT_SETTINGS_NOT_FOUND`
    /// siempre 404.
    /// </summary>
    private IActionResult MapError<T>(Response<T> response)
    {
        return response.Message switch
        {
            "COMPANY_REQUIRED" or "USER_REQUIRED" => Unauthorized(response),
            "TICKET_ATTACHMENT_SETTINGS_NOT_FOUND" => NotFound(response),
            "CONFIG_ALREADY_EXISTS" => Conflict(response),
            _ => BadRequest(response)
        };
    }

    /// <summary>
    /// Restores a logically deleted ticket attachment settings configuration (SPEC 41). Restricted to the SuperAdmin role.
    /// </summary>
    /// <param name="id">The ticket attachment settings configuration identifier.</param>
    /// <param name="companyId">Optional explicit company, honored only for SuperAdmin.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(
        Guid id,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new RestoreTicketAttachmentSettingsCommand(id, companyId), cancellationToken);
        return this.ToRestoreResult(response);
    }
}
