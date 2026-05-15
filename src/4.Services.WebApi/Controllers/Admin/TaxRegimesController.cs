using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.UseCases.Admin.TaxRegimes.Commands;
using JOIN.Application.UseCases.Admin.TaxRegimes.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;



namespace JOIN.Presentation.Controllers.Admin;



[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[PermissionResource("TaxRegimes")]
public class TaxRegimesController(ISender sender) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<TaxRegimeDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? pageNumber, [FromQuery] int? pageSize,
        [FromQuery] string? code, [FromQuery] string? name, [FromQuery] bool? isActive,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetTaxRegimesQuery(pageNumber, pageSize, code, name, isActive), cancellationToken);
        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<TaxRegimeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetTaxRegimeByIdQuery(id), cancellationToken);
        if (!response.IsSuccess) return response.Message == "TAX_REGIME_NOT_FOUND" ? NotFound(response) : BadRequest(response);
        return Ok(response);
    }

    [HttpPost]
    [ProducesResponseType(typeof(Response<TaxRegimeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateTaxRegimeCommand command, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(command, cancellationToken);
        if (!response.IsSuccess)
            return response.Message is "TAX_REGIME_CODE_IN_USE" or "TAX_REGIME_NAME_IN_USE" ? Conflict(response) : BadRequest(response);
        return CreatedAtAction(nameof(GetById), new { id = response.Data?.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<TaxRegimeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTaxRegimeCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Id != Guid.Empty && command.Id != id)
            return BadRequest(Response<TaxRegimeDto>.Error("ROUTE_BODY_ID_MISMATCH", ["The route id must match the request body id."]));
        var response = await _sender.Send(command with { Id = id }, cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "TAX_REGIME_NOT_FOUND") return NotFound(response);
            if (response.Message is "TAX_REGIME_CODE_IN_USE" or "TAX_REGIME_NAME_IN_USE") return Conflict(response);
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
        var response = await _sender.Send(new DeleteTaxRegimeCommand(id), cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "TAX_REGIME_NOT_FOUND") return NotFound(response);
            if (response.Message == "TAX_REGIME_IN_USE") return Conflict(response);
            return BadRequest(response);
        }
        return Ok(response);
    }
}
