using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.UseCases.Common.StreetTypes.Commands;
using JOIN.Application.UseCases.Common.StreetTypes.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers;

/// <summary>
/// API controller for managing street type catalog entities.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class StreetTypesController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator;

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<StreetTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var response = await _mediator.Send(new GetStreetTypeByIdQuery(id));
        if (!response.IsSuccess)
        {
            return NotFound(response);
        }

        return Ok(response);
    }

    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<StreetTypeListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPaged([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10, [FromQuery] string? searchTerm = null)
    {
        var response = await _mediator.Send(new GetStreetTypesPagedQuery(pageNumber, pageSize, searchTerm));
        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    [HttpPost]
    [ProducesResponseType(typeof(Response<StreetTypeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateStreetTypeCommand command)
    {
        var response = await _mediator.Send(command);
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

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<StreetTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStreetTypeCommand command)
    {
        var request = command with { Id = id };
        var response = await _mediator.Send(request);

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

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var response = await _mediator.Send(new DeleteStreetTypeCommand(id));
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
