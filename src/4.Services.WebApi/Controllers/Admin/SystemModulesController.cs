using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.UseCases.Admin.SystemModules.Commands;
using JOIN.Application.UseCases.Admin.SystemModules.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Presentation.Controllers.Admin;

/// <summary>
/// Exposes REST endpoints for managing global system modules.
/// Access is restricted to SuperAdmin users only.
/// </summary>
[ApiController]
[Authorize(Roles = "SuperAdmin")]
[Route("api/admin/[controller]")]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[PermissionResource("SystemModules")]
public class SystemModulesController(ISender sender) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    /// <summary>
    /// Retrieves a paginated list of system modules.
    /// </summary>
    /// <param name="pageNumber">Optional page number. When omitted, the configured default value is used.</param>
    /// <param name="pageSize">Optional page size. When omitted, the configured default value is used.</param>
    /// <param name="name">Optional partial-match filter applied to the module name.</param>
    /// <param name="isActive">Optional exact-match filter applied to the active flag.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the query is being processed.</param>
    /// <returns>A standardized paged response containing the matching modules.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<SystemModuleDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? name = null,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(
            new GetSystemModulesQuery(pageNumber, pageSize, name, isActive),
            cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Retrieves a single system module by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the system module to retrieve.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the query is being processed.</param>
    /// <returns>A standardized response containing the requested system module when it exists.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<SystemModuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetSystemModuleByIdQuery(id), cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "SYSTEM_MODULE_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates a new system module.
    /// </summary>
    /// <param name="command">The creation payload containing the system module data to persist.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the create command is being processed.</param>
    /// <returns>A <c>201 Created</c> response containing the newly created system module resource.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Response<SystemModuleDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSystemModuleCommand command,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(command, cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "SYSTEM_MODULE_NAME_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.Data!.Id }, response);
    }

    /// <summary>
    /// Updates an existing system module using the route identifier as the authoritative resource key.
    /// </summary>
    /// <param name="id">The unique identifier of the system module to update.</param>
    /// <param name="command">The update payload containing the desired system module state.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the update command is being processed.</param>
    /// <returns>A standardized response containing the updated system module resource.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<SystemModuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateSystemModuleCommand command,
        CancellationToken cancellationToken = default)
    {
        var request = command with { Id = id };
        var response = await _sender.Send(request, cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "SYSTEM_MODULE_NOT_FOUND")
            {
                return NotFound(response);
            }

            if (response.Message == "SYSTEM_MODULE_NAME_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Deletes a system module.
    /// </summary>
    /// <param name="id">The unique identifier of the system module to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the delete command is being processed.</param>
    /// <returns>A standardized response containing the deleted system module identifier.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new DeleteSystemModuleCommand(id), cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "SYSTEM_MODULE_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }
}