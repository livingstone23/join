using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadCompanySettings.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadCompanySettings.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Customers.LeadSettings;

/// <summary>
/// Lead module rules of the token's company (SPEC 59): duplicates policy, identification for customers and lead code.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("LeadSettings")]
public class LeadCompanySettingsController(IMediator mediator) : ControllerBase
{
    /// <summary>The company's row, or the defaults (<c>isDefault = true</c>) when it has none.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<LeadCompanySettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] Guid? companyId = null, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetLeadCompanySettingsQuery(companyId), cancellationToken));

    /// <summary>Creates or updates the company's row.</summary>
    [HttpPut]
    [ProducesResponseType(typeof(Response<LeadCompanySettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Save([FromBody] SaveLeadCompanySettingsCommand command, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(command, cancellationToken));
}
