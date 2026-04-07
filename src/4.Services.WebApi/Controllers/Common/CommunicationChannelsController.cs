using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.UseCases.Common.CommunicationChannels.Commands;
using JOIN.Application.UseCases.Common.CommunicationChannels.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;



namespace JOIN.Services.WebApi.Controllers.Common;



/// <summary>
/// Exposes REST endpoints for managing the communication channel catalog.
/// The controller remains a thin transport layer and delegates all command/query execution to MediatR handlers.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[PermissionResource("CommunicationChannels")]
public class CommunicationChannelsController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    /// <summary>
    /// Retrieves a single communication channel by its unique identifier.
    /// This endpoint is useful for maintenance screens that need the complete persisted channel definition before editing.
    /// </summary>
    /// <param name="id">The unique identifier of the communication channel to retrieve.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the query is executing.</param>
    /// <returns>A standardized response containing the requested communication channel.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<CommunicationChannelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetCommunicationChannelByIdQuery(id), cancellationToken);
        if (!response.IsSuccess)
        {
            return NotFound(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Returns a paginated list of communication channels with optional filtering.
    /// This endpoint supports catalog grids and administrative lookups that require server-side paging and search.
    /// </summary>
    /// <param name="pageNumber">The 1-based page number to retrieve.</param>
    /// <param name="pageSize">The maximum amount of rows to return on the requested page.</param>
    /// <param name="searchTerm">Optional text used to filter the channel list.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the paged query executes.</param>
    /// <returns>A standardized paged response containing the matching communication channels.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<CommunicationChannelListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetCommunicationChannelsPagedQuery(pageNumber, pageSize, searchTerm), cancellationToken);
        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates a new communication channel catalog entry.
    /// Duplicate channel names are rejected with a conflict response to preserve catalog consistency.
    /// </summary>
    /// <param name="command">The creation payload containing the communication channel data.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the create command is being processed.</param>
    /// <returns>A `201 Created` response containing the newly persisted communication channel.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Response<CommunicationChannelDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCommunicationChannelCommand command, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(command, cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "COMMUNICATIONCHANNEL_NAME_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.Data!.Id }, response);
    }

    /// <summary>
    /// Updates an existing communication channel identified by the route id.
    /// The endpoint returns not-found, validation, or conflict responses depending on the result produced by the Application layer.
    /// </summary>
    /// <param name="id">The unique identifier of the communication channel to update.</param>
    /// <param name="command">The update payload containing the new communication channel values.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the update command is running.</param>
    /// <returns>A standardized response containing the updated communication channel.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<CommunicationChannelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCommunicationChannelCommand command, CancellationToken cancellationToken = default)
    {
        var request = command with { Id = id };
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "COMMUNICATIONCHANNEL_NOT_FOUND")
            {
                return NotFound(response);
            }

            if (response.Message == "COMMUNICATIONCHANNEL_NAME_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Performs a soft delete over a communication channel catalog entry.
    /// The delete command preserves the standardized response contract and reports whether the target record was missing or invalid for deletion.
    /// </summary>
    /// <param name="id">The unique identifier of the communication channel to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the delete command executes.</param>
    /// <returns>A standardized response containing the deleted communication channel identifier.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new DeleteCommunicationChannelCommand(id), cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "COMMUNICATIONCHANNEL_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }
}
