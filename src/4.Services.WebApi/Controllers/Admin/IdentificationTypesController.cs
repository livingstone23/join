using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.UseCases.Admin.IdentificationTypes.Commands;
using JOIN.Application.UseCases.Admin.IdentificationTypes.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Admin;

/// <summary>
/// Exposes REST endpoints for managing identification document types.
/// The controller remains intentionally thin and delegates all business rules and persistence concerns to the Application layer through MediatR.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Route("api/admin/[controller]")]
[Produces("application/json")]
[PermissionResource("IdentificationTypes")]
public class IdentificationTypesController(ISender sender) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    /// <summary>
    /// Retrieves a paginated list of identification types.
    /// Optional filters can be applied using partial-match text criteria and creation-date filters.
    /// </summary>
    /// <param name="pageNumber">Optional page number. When omitted, the configured default value is used.</param>
    /// <param name="pageSize">Optional page size. When omitted, the configured default value is used.</param>
    /// <param name="name">Optional partial-match filter applied to the identification type name.</param>
    /// <param name="created">Optional exact creation-day filter.</param>
    /// <param name="createdFrom">Optional inclusive lower bound used to filter by creation date.</param>
    /// <param name="createdTo">Optional inclusive upper bound used to filter by creation date.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the query is being processed.</param>
    /// <returns>A standardized paged response containing the matching identification types.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<IdentificationTypeListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? name = null,
        [FromQuery] DateTime? created = null,
        [FromQuery] DateTime? createdFrom = null,
        [FromQuery] DateTime? createdTo = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(
            new GetIdentificationTypesQuery(pageNumber, pageSize, name, created, createdFrom, createdTo),
            cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Retrieves a single identification type by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the identification type to retrieve.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the query is being processed.</param>
    /// <returns>A standardized response containing the requested identification type when it exists.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<IdentificationTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetIdentificationTypeByIdQuery(id), cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "IDENTIFICATION_TYPE_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates a new identification type.
    /// </summary>
    /// <param name="command">The creation payload containing the identification type data to persist.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the create command is being processed.</param>
    /// <returns>A <c>201 Created</c> response containing the newly created identification type resource.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Response<IdentificationTypeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateIdentificationTypeCommand command,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(command, cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "IDENTIFICATION_TYPE_NAME_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.Data!.Id }, response);
    }

    /// <summary>
    /// Updates an existing identification type using the route identifier as the authoritative resource key.
    /// </summary>
    /// <param name="id">The unique identifier of the identification type to update.</param>
    /// <param name="command">The update payload containing the desired identification type state.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the update command is being processed.</param>
    /// <returns>A standardized response containing the updated identification type resource.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<IdentificationTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateIdentificationTypeCommand command,
        CancellationToken cancellationToken = default)
    {
        var request = command with { Id = id };
        var response = await _sender.Send(request, cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "IDENTIFICATION_TYPE_NOT_FOUND")
            {
                return NotFound(response);
            }

            if (response.Message == "IDENTIFICATION_TYPE_NAME_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Performs a soft delete over an identification type.
    /// </summary>
    /// <param name="id">The unique identifier of the identification type to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the delete command is being processed.</param>
    /// <returns>A standardized response containing the deleted identification type identifier.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new DeleteIdentificationTypeCommand(id), cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "IDENTIFICATION_TYPE_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }
}