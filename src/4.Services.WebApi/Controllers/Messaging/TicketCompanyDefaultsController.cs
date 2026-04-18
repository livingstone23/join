using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Commands;
using JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Messaging;

/// <summary>
/// Exposes REST endpoints for tenant ticket default configuration management.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
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
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetTicketCompanyDefaultsQuery(), cancellationToken);

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
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetTicketCompanyDefaultByIdQuery(id), cancellationToken);

        if (!response.IsSuccess)
        {
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
            if (response.Message == "TICKET_COMPANY_DEFAULT_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }
}
