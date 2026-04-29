using System;
using System.Threading.Tasks;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.UseCases.Security.SystemOptions.Commands;
using JOIN.Application.UseCases.Security.SystemOptions.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Security;

/// <summary>
/// Exposes RESTful endpoints for the administration of global system options (SystemOptions).
/// This controller enforces SuperAdmin-only access and delegates all business logic to the Application layer via MediatR.
/// Endpoints support paginated listing, retrieval, creation, update, and logical deletion (soft delete) of system options.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = "SuperAdmin")]
public class SystemOptionsController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Retrieves a paginated list of all system options in the platform.
    /// This endpoint is intended for administrative UIs and supports server-side paging for large option catalogs.
    /// </summary>
    /// <param name="pageNumber">The page number to retrieve (1-based).</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <returns>A standardized paged response containing system option list items.</returns>
    [HttpGet]
    public async Task<ActionResult<Response<PagedResult<SystemOptionListItemDto>>>> GetPaged([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
        => Ok(await mediator.Send(new GetSystemOptionsPagedQuery(pageNumber, pageSize)));

    /// <summary>
    /// Retrieves the details of a specific system option by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier (GUID) of the system option to retrieve.</param>
    /// <returns>A standardized response containing the system option details, or 404 if not found.</returns>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Response<SystemOptionDto>>> GetById(Guid id)
        => Ok(await mediator.Send(new GetSystemOptionByIdQuery(id)));

    /// <summary>
    /// Creates a new system option with the provided attributes.
    /// The creation logic, including validation and persistence, is handled in the Application layer.
    /// </summary>
    /// <param name="command">The payload containing the new system option data.</param>
    /// <returns>A standardized response containing the created system option.</returns>
    [HttpPost]
    public async Task<ActionResult<Response<SystemOptionDto>>> Create([FromBody] CreateSystemOptionCommand command)
        => Ok(await mediator.Send(command));

    /// <summary>
    /// Updates an existing system option identified by its unique identifier.
    /// The update logic, including validation and persistence, is handled in the Application layer.
    /// </summary>
    /// <param name="id">The unique identifier (GUID) of the system option to update.</param>
    /// <param name="command">The payload containing the updated system option data.</param>
    /// <returns>A standardized response containing the updated system option, or 404 if not found.</returns>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<Response<SystemOptionDto>>> Update(Guid id, [FromBody] UpdateSystemOptionCommand command)
        => Ok(await mediator.Send(command with { Id = id }));

    /// <summary>
    /// Performs a logical (soft) delete of a system option by its unique identifier.
    /// The option is not physically removed from the database, but marked as deleted for audit and recovery purposes.
    /// </summary>
    /// <param name="id">The unique identifier (GUID) of the system option to delete.</param>
    /// <returns>A standardized response indicating the result of the delete operation.</returns>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<Response<Guid>>> Delete(Guid id)
        => Ok(await mediator.Send(new DeleteSystemOptionCommand(id)));
}
