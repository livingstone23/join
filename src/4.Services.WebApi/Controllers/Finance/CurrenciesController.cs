using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Finance;
using JOIN.Application.UseCases.Finance.Currencies.Commands;
using JOIN.Application.UseCases.Finance.Currencies.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Finance;

/// <summary>
/// The global ISO currency catalog (SPEC 56). SuperAdmin only, like the <c>system-wide</c> endpoints.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("Currencies")]
[Authorize(Roles = "SuperAdmin")]
public class CurrenciesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<CurrencyDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] bool? includeDeleted = null,
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        this.ToFinanceResult(await mediator.Send(new GetCurrenciesQuery(search, isActive, includeDeleted, pageNumber, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<CurrencyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] bool? includeDeleted = null, CancellationToken cancellationToken = default) =>
        this.ToFinanceResult(await mediator.Send(new GetCurrencyByIdQuery(id, includeDeleted), cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(Response<CurrencyDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCurrencyCommand command, CancellationToken cancellationToken = default) =>
        this.ToFinanceCreated(await mediator.Send(command, cancellationToken));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<CurrencyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCurrencyCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest(Response<CurrencyDto>.Error("INVALID_CURRENCY_ID", ["Route id and payload id must match."]));
        }

        return this.ToFinanceResult(await mediator.Send(command with { Id = id }, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        this.ToFinanceResult(await mediator.Send(new DeleteCurrencyCommand(id), cancellationToken));

    /// <summary>Restores a logically deleted currency (SPEC 41-post).</summary>
    [HttpPost("{id:guid}/restore")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken = default) =>
        this.ToRestoreResult(await mediator.Send(new RestoreCurrencyCommand(id), cancellationToken));
}
