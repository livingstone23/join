using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.UseCases.Common.Countries.Commands;
using JOIN.Application.UseCases.Common.Countries.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers;

/// <summary>
/// API controller for managing country catalog entities.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class CountryController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator;

    /// <summary>
    /// Retrieves a country by id.
    /// </summary>
    /// <param name="id">The country identifier.</param>
    /// <returns>A standardized response with the requested country.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<CountryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var response = await _mediator.Send(new GetCountryByIdQuery(id));

        if (!response.IsSuccess)
        {
            return NotFound(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Retrieves a paginated list of countries with optional search by name.
    /// </summary>
    /// <param name="pageNumber">The requested page number.</param>
    /// <param name="pageSize">The requested page size.</param>
    /// <param name="searchTerm">Optional search term to filter by name.</param>
    /// <returns>A standardized paginated response.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<CountryListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? searchTerm = null)
    {
        var response = await _mediator.Send(new GetCountriesPagedQuery(pageNumber, pageSize, searchTerm));

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates a new country catalog item.
    /// </summary>
    /// <param name="command">The creation payload.</param>
    /// <returns>A standardized response with the created country.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Response<CountryDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCountryCommand command)
    {
        var response = await _mediator.Send(command);

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
    /// Updates an existing country catalog item.
    /// </summary>
    /// <param name="id">The country identifier to update.</param>
    /// <param name="command">The update payload.</param>
    /// <returns>A standardized response with the updated country.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<CountryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCountryCommand command)
    {
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest(Response<CountryDto>.Error("INVALID_COUNTRY_ID", ["Route id and payload id must match."]));
        }

        var request = command with { Id = id };
        var response = await _mediator.Send(request);

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
    /// Performs a soft delete for a country catalog item.
    /// </summary>
    /// <param name="id">The country identifier to delete.</param>
    /// <returns>A standardized response containing the deleted id.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var response = await _mediator.Send(new DeleteCountryCommand(id));

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
