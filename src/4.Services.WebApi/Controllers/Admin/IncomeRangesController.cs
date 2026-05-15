using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.UseCases.Admin.IncomeRanges.Commands;
using JOIN.Application.UseCases.Admin.IncomeRanges.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;



namespace JOIN.Presentation.Controllers.Admin;



[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[PermissionResource("IncomeRanges")]
public class IncomeRangesController(ISender sender) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<IncomeRangeDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromQuery] string? displayName,
        [FromQuery] string? currencyCode,
        [FromQuery] bool? isActive,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetIncomeRangesQuery(pageNumber, pageSize, displayName, currencyCode, isActive), cancellationToken);
        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<IncomeRangeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetIncomeRangeByIdQuery(id), cancellationToken);
        if (!response.IsSuccess)
            return response.Message == "INCOME_RANGE_NOT_FOUND" ? NotFound(response) : BadRequest(response);
        return Ok(response);
    }

    [HttpPost]
    [ProducesResponseType(typeof(Response<IncomeRangeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateIncomeRangeCommand command, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(command, cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "INCOME_RANGE_DISPLAY_NAME_IN_USE")
                return Conflict(response);
            return BadRequest(response);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.Data?.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<IncomeRangeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateIncomeRangeCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Id != Guid.Empty && command.Id != id)
            return BadRequest(Response<IncomeRangeDto>.Error("ROUTE_BODY_ID_MISMATCH", ["The route id must match the request body id."]));
        var response = await _sender.Send(command with { Id = id }, cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "INCOME_RANGE_NOT_FOUND")
                return NotFound(response);
            if (response.Message == "INCOME_RANGE_DISPLAY_NAME_IN_USE")
                return Conflict(response);
            return BadRequest(response);
        }

        return Ok(response);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new DeleteIncomeRangeCommand(id), cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "INCOME_RANGE_NOT_FOUND")
                return NotFound(response);
            if (response.Message == "INCOME_RANGE_IN_USE")
                return Conflict(response);
            return BadRequest(response);
        }

        return Ok(response);
    }
}
