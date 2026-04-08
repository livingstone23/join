using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.UseCases.Admin.Areas.Commands;
using JOIN.Application.UseCases.Admin.Areas.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Admin;

/// <summary>
/// Exposes REST endpoints for managing tenant-scoped functional areas.
/// The controller remains intentionally thin and delegates all business rules and persistence concerns to the Application layer through MediatR.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[PermissionResource("Areas")]
public class AreaController(ISender sender) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    /// <summary>
    /// Retrieves a paginated area list for the tenant identified by the <c>X-Company-Id</c> header.
    /// Optional filters can be applied by area name and inclusive creation-date range.
    /// </summary>
    /// <param name="companyId">The tenant identifier extracted from the <c>X-Company-Id</c> header.</param>
    /// <param name="pageNumber">Optional page number. When omitted, the configured default value is used.</param>
    /// <param name="pageSize">Optional page size. When omitted, the configured default value is used.</param>
    /// <param name="name">Optional partial-match filter applied to the area name.</param>
    /// <param name="createdFrom">Optional inclusive lower bound used to filter by creation date.</param>
    /// <param name="createdTo">Optional inclusive upper bound used to filter by creation date.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the query is being processed.</param>
    /// <returns>A standardized paged response containing the matching area collection.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<AreaListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? name = null,
        [FromQuery] DateTime? createdFrom = null,
        [FromQuery] DateTime? createdTo = null,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            return BadRequest(Response<PagedResult<AreaListItemDto>>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]));
        }

        var response = await _sender.Send(
            new GetAreasQuery(companyId, pageNumber, pageSize, name, createdFrom, createdTo),
            cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Retrieves a single area by its unique identifier within the tenant identified by the <c>X-Company-Id</c> header.
    /// </summary>
    /// <param name="id">The unique identifier of the area to retrieve.</param>
    /// <param name="companyId">The tenant identifier extracted from the <c>X-Company-Id</c> header.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the query is being processed.</param>
    /// <returns>A standardized response containing the requested area when it exists.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<AreaDto>), StatusCodes.Status200OK)]
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
            return BadRequest(Response<AreaDto>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]));
        }

        var response = await _sender.Send(new GetAreaByIdQuery(id, companyId), cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "AREA_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates a new area within the tenant identified by the <c>X-Company-Id</c> header.
    /// </summary>
    /// <param name="companyId">The tenant identifier extracted from the <c>X-Company-Id</c> header.</param>
    /// <param name="command">The creation payload containing the area data to persist.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the create command is being processed.</param>
    /// <returns>A <c>201 Created</c> response containing the newly created area resource.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Response<AreaDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromBody] CreateAreaCommand command,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            return BadRequest(Response<AreaDto>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]));
        }

        var request = command with { CompanyId = companyId };
        var response = await _sender.Send(request, cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "AREA_NAME_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.Data!.Id }, response);
    }

    /// <summary>
    /// Updates an existing area using the route identifier as the authoritative resource key and the <c>X-Company-Id</c> header as the tenant scope.
    /// </summary>
    /// <param name="id">The unique identifier of the area to update.</param>
    /// <param name="companyId">The tenant identifier extracted from the <c>X-Company-Id</c> header.</param>
    /// <param name="command">The update payload containing the desired area state.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the update command is being processed.</param>
    /// <returns>A standardized response containing the updated area resource.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<AreaDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromBody] UpdateAreaCommand command,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            return BadRequest(Response<AreaDto>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]));
        }

        var request = command with { Id = id, CompanyId = companyId };
        var response = await _sender.Send(request, cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "AREA_NOT_FOUND")
            {
                return NotFound(response);
            }

            if (response.Message == "AREA_NAME_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Performs a soft delete over an area that belongs to the tenant identified by the <c>X-Company-Id</c> header.
    /// </summary>
    /// <param name="id">The unique identifier of the area to delete.</param>
    /// <param name="companyId">The tenant identifier extracted from the <c>X-Company-Id</c> header.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the delete command is being processed.</param>
    /// <returns>A standardized response containing the deleted area identifier.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
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

        var response = await _sender.Send(new DeleteAreaCommand(id, companyId), cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "AREA_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }
}
