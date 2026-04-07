using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.UseCases.Common.StreetTypes.Commands;
using JOIN.Application.UseCases.Common.StreetTypes.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers;

/// <summary>
/// Exposes REST endpoints for managing the street type catalog.
/// The controller handles routing and response translation while the Application layer enforces the business rules.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class StreetTypesController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    /// <summary>
    /// Retrieves a street type catalog entry by its unique identifier.
    /// This endpoint is commonly used by maintenance forms that need the current persisted state before an edit operation.
    /// </summary>
    /// <param name="id">The unique identifier of the street type to retrieve.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the read query is being processed.</param>
    /// <returns>A standardized response containing the requested street type when it exists.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<StreetTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetStreetTypeByIdQuery(id), cancellationToken);
        if (!response.IsSuccess)
        {
            return NotFound(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Returns a paginated list of street types with optional text filtering.
    /// This endpoint is designed for lookup grids and administrative screens that require server-side paging.
    /// </summary>
    /// <param name="pageNumber">The 1-based page number to retrieve.</param>
    /// <param name="pageSize">The maximum number of records to return for the requested page.</param>
    /// <param name="searchTerm">Optional text used to filter the street type catalog.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the paged query executes.</param>
    /// <returns>A standardized paged response containing the matching street type rows.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<StreetTypeListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetStreetTypesPagedQuery(pageNumber, pageSize, searchTerm), cancellationToken);
        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates a new street type catalog entry.
    /// The Application layer validates uniqueness for both the name and abbreviation before persisting the new record.
    /// </summary>
    /// <param name="command">The creation payload containing the street type data.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the create command is being handled.</param>
    /// <returns>A `201 Created` response containing the newly created street type resource.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Response<StreetTypeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateStreetTypeCommand command, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(command, cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "STREETTYPE_NAME_IN_USE" || response.Message == "STREETTYPE_ABBREVIATION_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.Data!.Id }, response);
    }

    /// <summary>
    /// Updates an existing street type identified by the route id.
    /// Validation, not-found handling, and uniqueness checks are delegated to the corresponding Application command handler.
    /// </summary>
    /// <param name="id">The unique identifier of the street type to update.</param>
    /// <param name="command">The update payload containing the new street type state.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the update command is executing.</param>
    /// <returns>A standardized response containing the updated street type.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<StreetTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStreetTypeCommand command, CancellationToken cancellationToken = default)
    {
        var request = command with { Id = id };
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "STREETTYPE_NOT_FOUND")
            {
                return NotFound(response);
            }

            if (response.Message == "STREETTYPE_NAME_IN_USE" || response.Message == "STREETTYPE_ABBREVIATION_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Performs a soft delete over a street type catalog record.
    /// The endpoint reports not-found and validation outcomes using the same standardized response contract as the rest of the API.
    /// </summary>
    /// <param name="id">The unique identifier of the street type to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the delete command runs.</param>
    /// <returns>A standardized response containing the identifier of the deleted street type.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new DeleteStreetTypeCommand(id), cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "STREETTYPE_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }
}
