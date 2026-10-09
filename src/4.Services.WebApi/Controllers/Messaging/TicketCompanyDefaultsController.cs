using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Commands;
using JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;



namespace JOIN.Services.WebApi.Controllers.Messaging;



/// <summary>
/// Exposes REST endpoints for tenant ticket default configuration management.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("TicketCompanyDefaults")]
public class TicketCompanyDefaultsController(ISender sender) : ControllerBase
{

    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    
    /// <summary>
    /// Retrieves the active tenant configuration list.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<IReadOnlyCollection<TicketCompanyDefaultDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool? includeDeleted = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetTicketCompanyDefaultsQuery(includeDeleted, companyId), cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "COMPANY_REQUIRED")
            {
                return Unauthorized(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Retrieves a paginated system-wide list of ticket default configurations across all companies.
    /// Access is restricted to SuperAdmin users only.
    /// </summary>
    [HttpGet("system-wide")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(Response<PagedResult<TicketCompanyDefaultDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSystemWide(
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? companyName = null,
        [FromQuery] string? startCode = null,
        [FromQuery] string? ticketStatusDefaultName = null,
        [FromQuery] string? ticketComplexityDefaultName = null,
        [FromQuery] string? complexityName = null,
        [FromQuery] string? timeUnitDefaultName = null,
        [FromQuery] string? areaName = null,
        [FromQuery] string? projectName = null,
        [FromQuery] string? channelName = null,
        [FromQuery(Name = "channetname")] string? channetname = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveComplexityName = string.IsNullOrWhiteSpace(ticketComplexityDefaultName)
            ? complexityName
            : ticketComplexityDefaultName;

        var effectiveChannelName = string.IsNullOrWhiteSpace(channelName)
            ? channetname
            : channelName;

        var response = await _sender.Send(
            new GetSystemWideTicketCompanyDefaultsQuery(
                pageNumber,
                pageSize,
                companyName,
                startCode,
                ticketStatusDefaultName,
                effectiveComplexityName,
                timeUnitDefaultName,
                areaName,
                projectName,
                effectiveChannelName),
            cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }


    /// <summary>
    /// Retrieves a single tenant configuration by identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<TicketCompanyDefaultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromQuery] bool? includeDeleted = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetTicketCompanyDefaultByIdQuery(id, includeDeleted, companyId), cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "COMPANY_REQUIRED")
            {
                return Unauthorized(response);
            }

            if (response.Message == "TICKET_COMPANY_DEFAULT_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates the tenant ticket default configuration.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<TicketCompanyDefaultDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateTicketCompanyDefaultDto dto, CancellationToken cancellationToken = default)
    {
        var command = new CreateTicketCompanyDefaultCommand
        {
            StartCode = dto.StartCode,
            CodeSequenceLength = dto.CodeSequenceLength,
            UsePersonalizedCode = dto.UsePersonalizedCode,
            TicketStatusDefaultId = dto.TicketStatusDefaultId,
            TicketComplexityDefaultId = dto.TicketComplexityDefaultId,
            TimeUnitDefaultId = dto.TimeUnitDefaultId,
            AreaDefaultId = dto.AreaDefaultId,
            ProjectDefaultId = dto.ProjectDefaultId,
            ChannelDefaultId = dto.ChannelDefaultId
        };

        var response = await _sender.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "COMPANY_REQUIRED")
            {
                return Unauthorized(response);
            }

            if (response.Message == "CONFIG_ALREADY_EXISTS")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.Data!.Id }, response);
    }

    /// <summary>
    /// Updates the tenant ticket default configuration.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<TicketCompanyDefaultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTicketCompanyDefaultDto dto, CancellationToken cancellationToken = default)
    {
        var command = new UpdateTicketCompanyDefaultCommand
        {
            Id = id,
            StartCode = dto.StartCode,
            CodeSequenceLength = dto.CodeSequenceLength,
            UsePersonalizedCode = dto.UsePersonalizedCode,
            TicketStatusDefaultId = dto.TicketStatusDefaultId,
            TicketComplexityDefaultId = dto.TicketComplexityDefaultId,
            TimeUnitDefaultId = dto.TimeUnitDefaultId,
            AreaDefaultId = dto.AreaDefaultId,
            ProjectDefaultId = dto.ProjectDefaultId,
            ChannelDefaultId = dto.ChannelDefaultId
        };

        var response = await _sender.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "COMPANY_REQUIRED")
            {
                return Unauthorized(response);
            }

            if (response.Message == "TICKET_COMPANY_DEFAULT_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Performs a logical delete over the tenant ticket default configuration.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new DeleteTicketCompanyDefaultCommand(id), cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "COMPANY_REQUIRED")
            {
                return Unauthorized(response);
            }

            if (response.Message == "TICKET_COMPANY_DEFAULT_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Restores a logically deleted ticket company default configuration (SPEC 41). Restricted to the SuperAdmin role.
    /// </summary>
    /// <param name="id">The ticket company default configuration identifier.</param>
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
        var response = await _sender.Send(new RestoreTicketCompanyDefaultCommand(id, companyId), cancellationToken);
        return this.ToRestoreResult(response);
    }
}
