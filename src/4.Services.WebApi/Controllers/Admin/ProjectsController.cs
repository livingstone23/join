using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.UseCases.Admin.Projects.Commands;
using JOIN.Application.UseCases.Admin.Projects.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Presentation.Controllers.Admin;

/// <summary>
/// Exposes REST endpoints for managing tenant-scoped projects.
/// The controller remains intentionally thin and delegates all business rules and persistence concerns to the Application layer through MediatR.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Route("api/admin/[controller]")]
[Produces("application/json")]
[PermissionResource("Projects")]
public class ProjectsController(ISender sender) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    /// <summary>
    /// Retrieves a paginated and filterable project list for the tenant identified by the <c>X-Company-Id</c> header.
    /// </summary>
    /// <param name="companyId">The tenant identifier extracted from the <c>X-Company-Id</c> header.</param>
    /// <param name="pageNumber">Optional page number. When omitted, the configured default value is used.</param>
    /// <param name="pageSize">Optional page size. When omitted, the configured default value is used.</param>
    /// <param name="name">Optional partial-match filter applied to the project name.</param>
    /// <param name="entityStatusId">Optional exact-match filter applied to the linked entity status.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the query is being processed.</param>
    /// <returns>A standardized paged response containing the matching project collection.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<ProjectDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? name = null,
        [FromQuery] Guid? entityStatusId = null,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            return BadRequest(Response<PagedResult<ProjectDto>>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]));
        }

        var response = await _sender.Send(
            new GetProjectsQuery(companyId, pageNumber, pageSize, name, entityStatusId),
            cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Retrieves a single tenant-scoped project by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the project to retrieve.</param>
    /// <param name="companyId">The tenant identifier extracted from the <c>X-Company-Id</c> header.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the query is being processed.</param>
    /// <returns>A standardized response containing the requested project when it exists.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<ProjectDto>), StatusCodes.Status200OK)]
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
            return BadRequest(Response<ProjectDto>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]));
        }

        var response = await _sender.Send(new GetProjectByIdQuery(id, companyId), cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "PROJECT_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates a new project within the tenant identified by the <c>X-Company-Id</c> header.
    /// </summary>
    /// <param name="companyId">The tenant identifier extracted from the <c>X-Company-Id</c> header.</param>
    /// <param name="command">The creation payload containing the project data to persist.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the create command is being processed.</param>
    /// <returns>A <c>201 Created</c> response containing the newly created project resource.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Response<ProjectDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromBody] CreateProjectCommand command,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            return BadRequest(Response<ProjectDto>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]));
        }

        var request = command with { CompanyId = companyId };
        var response = await _sender.Send(request, cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "PROJECT_NAME_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.Data?.Id }, response);
    }

    /// <summary>
    /// Updates an existing project using the route identifier as the authoritative key and the <c>X-Company-Id</c> header as the tenant scope.
    /// </summary>
    /// <param name="id">The unique identifier of the project to update.</param>
    /// <param name="companyId">The tenant identifier extracted from the <c>X-Company-Id</c> header.</param>
    /// <param name="command">The update payload containing the desired project state.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the update command is being processed.</param>
    /// <returns>A standardized response containing the updated project resource.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<ProjectDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromBody] UpdateProjectCommand command,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            return BadRequest(Response<ProjectDto>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]));
        }

        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest(Response<ProjectDto>.Error(
                "ROUTE_BODY_ID_MISMATCH",
                ["The route id must match the request body id."]));
        }

        var request = command with { Id = id, CompanyId = companyId };
        var response = await _sender.Send(request, cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "PROJECT_NOT_FOUND")
            {
                return NotFound(response);
            }

            if (response.Message == "PROJECT_NAME_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Deletes a tenant-scoped project.
    /// </summary>
    /// <param name="id">The unique identifier of the project to delete.</param>
    /// <param name="companyId">The tenant identifier extracted from the <c>X-Company-Id</c> header.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the delete command is being processed.</param>
    /// <returns>A standardized response containing the deleted project identifier.</returns>
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

        var response = await _sender.Send(new DeleteProjectCommand(id, companyId), cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "PROJECT_NOT_FOUND")
            {
                return NotFound(response);
            }

            if (response.Message == "PROJECT_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }
}