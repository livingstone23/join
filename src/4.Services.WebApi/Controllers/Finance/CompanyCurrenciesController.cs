using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Finance;
using JOIN.Application.UseCases.Finance.CompanyCurrencies.Commands;
using JOIN.Application.UseCases.Finance.CompanyCurrencies.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Finance;

/// <summary>
/// The currencies of the company of the token, with the base-currency indicator (SPEC 56).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("CompanyCurrencies")]
public class CompanyCurrenciesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Response<IReadOnlyList<CompanyCurrencyDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool? includeInactive = null,
        [FromQuery] bool? includeDeleted = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default) =>
        this.ToFinanceResult(await mediator.Send(new GetCompanyCurrenciesQuery(includeInactive, includeDeleted, companyId), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<CompanyCurrencyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromQuery] bool? includeDeleted = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default) =>
        this.ToFinanceResult(await mediator.Send(new GetCompanyCurrencyByIdQuery(id, includeDeleted, companyId), cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(Response<CompanyCurrencyDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCompanyCurrencyCommand command, CancellationToken cancellationToken = default) =>
        this.ToFinanceCreated(await mediator.Send(command, cancellationToken));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<CompanyCurrencyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCompanyCurrencyCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest(Response<CompanyCurrencyDto>.Error("INVALID_COMPANY_CURRENCY_ID", ["Route id and payload id must match."]));
        }

        return this.ToFinanceResult(await mediator.Send(command with { Id = id }, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        this.ToFinanceResult(await mediator.Send(new DeleteCompanyCurrencyCommand(id), cancellationToken));

    /// <summary>Restores a logically deleted company currency (SPEC 41-post). Restricted to the SuperAdmin role.</summary>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid id, [FromQuery] Guid? companyId = null, CancellationToken cancellationToken = default) =>
        this.ToRestoreResult(await mediator.Send(new RestoreCompanyCurrencyCommand(id, companyId), cancellationToken));
}
