using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.Leads;
using JOIN.Application.UseCases.Customers.Leads.Documents;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Customers.LeadSettings;

/// <summary>Attachment limits of the token's company for leads (SPEC 62), "Adjuntos" tab of the commercial settings.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("LeadSettings")]
public class LeadAttachmentSettingsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Response<LeadAttachmentSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get([FromQuery] Guid? companyId = null, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetLeadAttachmentSettingsQuery(companyId), cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(Response<LeadAttachmentSettingsDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateLeadAttachmentSettingsCommand command, CancellationToken cancellationToken = default) =>
        this.ToCommercialCreated(await mediator.Send(command, cancellationToken));

    [HttpPut]
    [ProducesResponseType(typeof(Response<LeadAttachmentSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update([FromBody] UpdateLeadAttachmentSettingsCommand command, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(command, cancellationToken));
}
