using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Finance;
using JOIN.Application.UseCases.Finance.ExchangeRates.Commands;
using JOIN.Application.UseCases.Finance.ExchangeRates.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Finance;

/// <summary>
/// Dated exchange rates of the company's parallel currencies (SPEC 56).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("ExchangeRates")]
public class ExchangeRatesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<ExchangeRateDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? currencyId = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] bool? includeDeleted = null,
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default) =>
        this.ToFinanceResult(await mediator.Send(new GetExchangeRatesQuery(currencyId, from, to, includeDeleted, pageNumber, pageSize, companyId), cancellationToken));

    /// <summary>The rate that applies to a currency on a date (today by default), SPEC 56 rule 4.</summary>
    [HttpGet("resolve")]
    [ProducesResponseType(typeof(Response<ExchangeRateResolutionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Resolve([FromQuery] Guid currencyId, [FromQuery] DateOnly? date = null, CancellationToken cancellationToken = default) =>
        this.ToFinanceResult(await mediator.Send(new ResolveExchangeRateQuery(currencyId, date), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<ExchangeRateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromQuery] bool? includeDeleted = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default) =>
        this.ToFinanceResult(await mediator.Send(new GetExchangeRateByIdQuery(id, includeDeleted, companyId), cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(Response<ExchangeRateDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateExchangeRateCommand command, CancellationToken cancellationToken = default) =>
        this.ToFinanceCreated(await mediator.Send(command, cancellationToken));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<ExchangeRateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateExchangeRateCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest(Response<ExchangeRateDto>.Error("INVALID_EXCHANGE_RATE_ID", ["Route id and payload id must match."]));
        }

        return this.ToFinanceResult(await mediator.Send(command with { Id = id }, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        this.ToFinanceResult(await mediator.Send(new DeleteExchangeRateCommand(id), cancellationToken));

    /// <summary>Restores a logically deleted exchange rate (SPEC 41-post). Restricted to the SuperAdmin role.</summary>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid id, [FromQuery] Guid? companyId = null, CancellationToken cancellationToken = default) =>
        this.ToRestoreResult(await mediator.Send(new RestoreExchangeRateCommand(id, companyId), cancellationToken));
}
