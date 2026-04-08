using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.UseCases.Admin.EntityStatuses.Commands;
using JOIN.Application.UseCases.Admin.EntityStatuses.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Admin;

/// <summary>
/// Exposes REST endpoints for managing administrative entity statuses.
/// The controller remains intentionally thin and delegates all business rules and persistence concerns to the Application layer through MediatR.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[PermissionResource("EntityStatuses")]
public class EntityStatusesController(ISender sender) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    /// <summary>
    /// Retrieves a paginated list of entity statuses.
    /// Optional filters can be applied using partial-match text criteria and an inclusive creation-date range.
    /// </summary>
    /// <param name="companyId">The tenant identifier extracted from the <c>X-Company-Id</c> header.</param>
    /// <param name="pageNumber">Optional page number. When omitted, the configured default value is used.</param>
    /// <param name="pageSize">Optional page size. When omitted, the configured default value is used.</param>
    /// <param name="entityStatuses">Optional partial-match filter applied to the status name.</param>
    /// <param name="moduleName">Optional secondary partial-match filter applied to the status description.</param>
    /// <param name="createdFrom">Optional inclusive lower bound used to filter by creation date.</param>
    /// <param name="createdTo">Optional inclusive upper bound used to filter by creation date.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the query is being processed.</param>
    /// <returns>A standardized paged response containing the matching entity statuses.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<EntityStatusListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        [FromQuery(Name = "entityStatuses")] string? entityStatuses = null,
        [FromQuery] string? moduleName = null,
        [FromQuery] DateTime? createdFrom = null,
        [FromQuery] DateTime? createdTo = null,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            return BadRequest(Response<PagedResult<EntityStatusListItemDto>>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]));
        }

        var response = await _sender.Send(
            new GetEntityStatusQuery(companyId, pageNumber, pageSize, entityStatuses, moduleName, createdFrom, createdTo),
            cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Retrieves a single entity status by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the entity status to retrieve.</param>
    /// <param name="companyId">The tenant identifier extracted from the <c>X-Company-Id</c> header.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the query is being processed.</param>
    /// <returns>A standardized response containing the requested entity status when it exists.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<EntityStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            return BadRequest(Response<EntityStatusDto>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]));
        }

        var response = await _sender.Send(new GetEntityStatusByIdQuery(id, companyId), cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "ENTITY_STATUS_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates a new entity status.
    /// </summary>
    /// <param name="companyId">The tenant identifier extracted from the <c>X-Company-Id</c> header.</param>
    /// <param name="command">The creation payload containing the entity status data to persist.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the create command is being processed.</param>
    /// <returns>A <c>201 Created</c> response containing the newly created entity status resource.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Response<EntityStatusDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromBody] CreateEntityStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            return BadRequest(Response<EntityStatusDto>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]));
        }

        var request = command with { CompanyId = companyId };
        var response = await _sender.Send(request, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message is "ENTITY_STATUS_NAME_IN_USE" or "ENTITY_STATUS_CODE_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.Data!.Id }, response);
    }

    /// <summary>
    /// Updates an existing entity status using the route identifier as the authoritative resource key.
    /// </summary>
    /// <param name="id">The unique identifier of the entity status to update.</param>
    /// <param name="companyId">The tenant identifier extracted from the <c>X-Company-Id</c> header.</param>
    /// <param name="command">The update payload containing the desired entity status state.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the update command is being processed.</param>
    /// <returns>A standardized response containing the updated entity status resource.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<EntityStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromBody] UpdateEntityStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            return BadRequest(Response<EntityStatusDto>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]));
        }

        var request = command with { Id = id, CompanyId = companyId };
        var response = await _sender.Send(request, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "ENTITY_STATUS_NOT_FOUND")
            {
                return NotFound(response);
            }

            if (response.Message is "ENTITY_STATUS_NAME_IN_USE" or "ENTITY_STATUS_CODE_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Performs a soft delete over an entity status.
    /// </summary>
    /// <param name="id">The unique identifier of the entity status to delete.</param>
    /// <param name="companyId">The tenant identifier extracted from the <c>X-Company-Id</c> header.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the delete command is being processed.</param>
    /// <returns>A standardized response containing the deleted entity status identifier.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            return BadRequest(Response<Guid>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]));
        }

        var response = await _sender.Send(new DeleteEntityStatusCommand(id, companyId), cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "ENTITY_STATUS_NOT_FOUND")
            {
                return NotFound(response);
            }

            if (response.Message == "ENTITY_STATUS_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }
}
