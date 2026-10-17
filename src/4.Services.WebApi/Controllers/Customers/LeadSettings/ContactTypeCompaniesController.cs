using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.UseCases.Customers.LeadSettings.ContactTypeCompanies.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.ContactTypeCompanies.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Customers.LeadSettings;

/// <summary>
/// The company's contact types (SPEC 59): created by the seed, the company renames, orders and activates them. No
/// <c>POST</c> or <c>DELETE</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("LeadSettings")]
public class ContactTypeCompaniesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Response<IReadOnlyList<ContactTypeCompanyDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool? includeInactive = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetContactTypeCompaniesQuery(includeInactive, companyId), cancellationToken));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<ContactTypeCompanyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateContactTypeCompanyCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest(Response<ContactTypeCompanyDto>.Error("INVALID_CONTACT_TYPE_ID", ["Route id and payload id must match."]));
        }

        return this.ToCommercialResult(await mediator.Send(command with { Id = id }, cancellationToken));
    }
}
