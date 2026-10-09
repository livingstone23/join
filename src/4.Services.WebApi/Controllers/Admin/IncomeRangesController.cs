using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.UseCases.Admin.IncomeRanges.Commands;
using JOIN.Application.UseCases.Admin.IncomeRanges.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using JOIN.Services.WebApi.Controllers;
using Microsoft.AspNetCore.Authorization;



namespace JOIN.Presentation.Controllers.Admin;



[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
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
        [FromQuery] bool? includeDeleted = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetIncomeRangesQuery(pageNumber, pageSize, displayName, currencyCode, isActive, includeDeleted, companyId), cancellationToken);
        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<IncomeRangeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] bool? includeDeleted = null, [FromQuery] Guid? companyId = null, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetIncomeRangeByIdQuery(id, includeDeleted, companyId), cancellationToken);
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
            if (response.Message is "INCOME_RANGE_DISPLAY_NAME_IN_USE" or "INCOME_RANGE_DISPLAY_ORDER_IN_USE")
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
            if (response.Message is "INCOME_RANGE_DISPLAY_NAME_IN_USE" or "INCOME_RANGE_DISPLAY_ORDER_IN_USE")
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

    /// <summary>
    /// Restores a logically deleted income range (SPEC 41). Restricted to the SuperAdmin role.
    /// </summary>
    /// <param name="id">The income range identifier.</param>
    /// <param name="companyId">Optional explicit company, honored only for SuperAdmin.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(
        Guid id,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new RestoreIncomeRangeCommand(id, companyId), cancellationToken);
        return this.ToRestoreResult(response);
    }
}
