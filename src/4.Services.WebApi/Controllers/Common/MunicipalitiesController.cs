using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.UseCases.Common.Municipalities.Commands;
using JOIN.Application.UseCases.Common.Municipalities.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Common;

/// <summary>
/// Exposes REST endpoints for managing the municipality catalog.
/// The controller only coordinates transport-level concerns while the Application layer handles validation and persistence rules.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Route("api/admin/[controller]")]
[Produces("application/json")]
[PermissionResource("Municipalities")]
public class MunicipalitiesController(ISender sender) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    /// <summary>
    /// Retrieves a paginated list of municipalities with optional filters by name, code, and province.
    /// </summary>
    /// <param name="pageNumber">The optional page number to retrieve.</param>
    /// <param name="pageSize">The optional page size to retrieve.</param>
    /// <param name="name">Optional municipality-name search term.</param>
    /// <param name="code">Optional exact municipality-code filter.</param>
    /// <param name="provinceId">Optional parent-province filter for dependent combo scenarios.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the paged query executes.</param>
    /// <returns>A standardized paged response containing the matching municipalities.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<MunicipalityDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? name = null,
        [FromQuery] string? code = null,
        [FromQuery] Guid? provinceId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetMunicipalitiesQuery(pageNumber, pageSize, name, code, provinceId), cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Retrieves a single municipality catalog record by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the municipality to retrieve.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the read query is running.</param>
    /// <returns>A standardized response containing the requested municipality when it exists.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<MunicipalityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetMunicipalityByIdQuery(id), cancellationToken);

        if (!response.IsSuccess)
        {
            return NotFound(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates a new municipality catalog entry.
    /// </summary>
    /// <param name="command">The creation payload containing the municipality data to persist.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the create command is being processed.</param>
    /// <returns>A <c>201 Created</c> response containing the newly created municipality resource.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Response<MunicipalityDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateMunicipalityCommand command, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message is "MUNICIPALITY_CODE_IN_USE" or "MUNICIPALITY_NAME_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.Data!.Id }, response);
    }

    /// <summary>
    /// Updates an existing municipality catalog entry while preserving the route identifier as the authoritative resource key.
    /// </summary>
    /// <param name="id">The unique identifier of the municipality to update.</param>
    /// <param name="command">The update payload containing the new municipality values.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the update command executes.</param>
    /// <returns>A standardized response containing the updated municipality.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<MunicipalityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateMunicipalityCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest(Response<MunicipalityDto>.Error("INVALID_MUNICIPALITY_ID", ["Route id and payload id must match."]));
        }

        var request = command with { Id = id };
        var response = await _sender.Send(request, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "MUNICIPALITY_NOT_FOUND")
            {
                return NotFound(response);
            }

            if (response.Message is "MUNICIPALITY_CODE_IN_USE" or "MUNICIPALITY_NAME_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Performs a soft delete over a municipality catalog entry.
    /// </summary>
    /// <param name="id">The unique identifier of the municipality to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the delete command is running.</param>
    /// <returns>A standardized response containing the identifier of the deleted municipality.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new DeleteMunicipalityCommand(id), cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "MUNICIPALITY_NOT_FOUND")
            {
                return NotFound(response);
            }

            if (response.Message == "MUNICIPALITY_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }
}
