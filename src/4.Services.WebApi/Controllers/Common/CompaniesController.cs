using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.UseCases.Common.Companies.Commands;
using JOIN.Application.UseCases.Common.Companies.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;



namespace JOIN.Services.WebApi.Controllers.Common;



/// <summary>
/// Exposes REST endpoints for managing the company catalog.
/// The controller is intentionally limited to HTTP orchestration concerns and delegates validation and business rules to MediatR handlers.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[PermissionResource("Companies")]
public class CompaniesController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    /// <summary>
    /// Retrieves a single company record by its unique identifier.
    /// This endpoint is typically used by detail and edit screens that need the persisted company payload before applying changes.
    /// </summary>
    /// <param name="id">The unique identifier of the company to retrieve.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the read operation is in progress.</param>
    /// <returns>A standardized response containing the requested company when it exists.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<CompanyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetCompanyByIdQuery(id), cancellationToken);
        if (!response.IsSuccess)
        {
            return NotFound(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Returns a paginated company list with optional text filtering.
    /// This endpoint supports management grids that need server-side pagination and search over the active catalog.
    /// </summary>
    /// <param name="pageNumber">The 1-based page number to retrieve.</param>
    /// <param name="pageSize">The amount of records that should be returned per page.</param>
    /// <param name="searchTerm">Optional free-text filter applied to the company catalog query.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the paged query executes.</param>
    /// <returns>A standardized paged response containing the requested company slice.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<CompanyListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetCompaniesPagedQuery(pageNumber, pageSize, searchTerm), cancellationToken);
        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates a new company catalog entry.
    /// The payload is validated in the Application layer, and duplicate tax identifiers are rejected with a conflict response.
    /// </summary>
    /// <param name="command">The creation payload containing the company data to persist.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the creation command is being handled.</param>
    /// <returns>A `201 Created` response containing the newly created company resource.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Response<CompanyDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCompanyCommand command, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(command, cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "COMPANY_TAXID_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.Data!.Id }, response);
    }

    /// <summary>
    /// Updates an existing company catalog entry identified by the route id.
    /// The endpoint preserves the current route-driven identity and returns validation, not-found, or conflict responses when appropriate.
    /// </summary>
    /// <param name="id">The unique identifier of the company to update.</param>
    /// <param name="command">The update payload containing the new company state.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the update command is being processed.</param>
    /// <returns>A standardized response containing the updated company data.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<CompanyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCompanyCommand command, CancellationToken cancellationToken = default)
    {
        var request = command with { Id = id };
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "COMPANY_NOT_FOUND")
            {
                return NotFound(response);
            }

            if (response.Message == "COMPANY_TAXID_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Performs a soft delete over an existing company catalog record.
    /// The underlying command keeps the standardized response contract and reports whether the company was missing or invalid for deletion.
    /// </summary>
    /// <param name="id">The unique identifier of the company to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the delete command is executing.</param>
    /// <returns>A standardized response containing the identifier of the deleted company.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new DeleteCompanyCommand(id), cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "COMPANY_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }
}
