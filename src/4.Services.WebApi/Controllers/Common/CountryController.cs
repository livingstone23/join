using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.UseCases.Common.Countries.Commands;
using JOIN.Application.UseCases.Common.Countries.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;



namespace JOIN.Services.WebApi.Controllers.Common;



/// <summary>
/// Exposes REST endpoints for managing the country catalog.
/// The controller only coordinates transport-level concerns while the Application layer handles validation and persistence rules.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[PermissionResource("Countries")]
public class CountryController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    /// <summary>
    /// Retrieves a single country catalog record by its unique identifier.
    /// This endpoint is typically consumed by edit forms and detail screens that need the current persisted values for a specific country.
    /// </summary>
    /// <param name="id">The unique identifier of the country to retrieve.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the read query is running.</param>
    /// <returns>A standardized response containing the requested country when it exists.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<CountryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetCountryByIdQuery(id), cancellationToken);

        if (!response.IsSuccess)
        {
            return NotFound(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Retrieves a paginated list of countries with optional text filtering.
    /// This endpoint is intended for maintenance grids that require server-side pagination and name-based search capabilities.
    /// </summary>
    /// <param name="pageNumber">The 1-based page number to retrieve.</param>
    /// <param name="pageSize">The maximum number of country rows to return for the requested page.</param>
    /// <param name="searchTerm">Optional text used to filter the list by country name.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the paged query executes.</param>
    /// <returns>A standardized paged response containing the matching countries.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<CountryListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetCountriesPagedQuery(pageNumber, pageSize, searchTerm), cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates a new country catalog entry.
    /// The request is validated in the Application layer and duplicate ISO codes are reported as conflicts to keep the catalog consistent.
    /// </summary>
    /// <param name="command">The creation payload containing the country data to persist.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the create command is being processed.</param>
    /// <returns>A `201 Created` response containing the newly created country resource.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Response<CountryDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCountryCommand command, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "COUNTRY_ISO_CODE_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.Data!.Id }, response);
    }

    /// <summary>
    /// Updates an existing country catalog entry while preserving the route identifier as the authoritative resource key.
    /// Validation, not-found handling, and uniqueness checks are delegated to the corresponding Application command handler.
    /// </summary>
    /// <param name="id">The unique identifier of the country to update.</param>
    /// <param name="command">The update payload containing the new country values.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the update command executes.</param>
    /// <returns>A standardized response containing the updated country.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<CountryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCountryCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest(Response<CountryDto>.Error("INVALID_COUNTRY_ID", ["Route id and payload id must match."]));
        }

        var request = command with { Id = id };
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "COUNTRY_NOT_FOUND")
            {
                return NotFound(response);
            }

            if (response.Message == "COUNTRY_ISO_CODE_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Performs a soft delete over a country catalog entry.
    /// The endpoint keeps the response contract explicit and reports not-found or validation failures when the delete command cannot be completed.
    /// </summary>
    /// <param name="id">The unique identifier of the country to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the delete command is running.</param>
    /// <returns>A standardized response containing the identifier of the deleted country.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new DeleteCountryCommand(id), cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "COUNTRY_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }
}
