using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common.Emails;
using JOIN.Application.UseCases.Common.Emails.Commands;
using JOIN.Application.UseCases.Common.Emails.Queries;
using JOIN.Domain.Security;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Common;

/// <summary>
/// SPEC 72 — the emails of a lead, under the <c>Leads</c> resource. Sending uses <c>CanUpdate</c>, like registering a
/// contact (SPEC 61); the lead is the one of the route and must be open and in the user's scope (SPEC 60).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/Leads/{leadId:guid}/emails")]
[Produces("application/json")]
[PermissionResource("Leads")]
public class LeadEmailsController(IMediator mediator) : ControllerBase
{
    /// <summary>Sends an email to the person of the lead and registers the contact in its timeline.</summary>
    [HttpPost]
    [RequirePermission(PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<PersonEmailSentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(Response<PersonEmailSentDto>), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Send(Guid leadId, [FromBody] SendLeadEmailCommand command, CancellationToken cancellationToken = default) =>
        this.ToPersonEmailCreated(await mediator.Send(command with { LeadId = leadId }, cancellationToken));

    /// <summary>The emails associated to the lead, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<PersonEmailMessageListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid leadId, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        this.ToPersonEmailResult(await mediator.Send(new GetPersonEmailsQuery(PersonEmailHistoryScope.Lead, leadId, pageNumber, pageSize), cancellationToken));

    /// <summary>The detail of an email of the lead.</summary>
    [HttpGet("{emailId:guid}")]
    [ProducesResponseType(typeof(Response<PersonEmailMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detail(Guid leadId, Guid emailId, CancellationToken cancellationToken = default) =>
        this.ToPersonEmailResult(await mediator.Send(new GetPersonEmailQuery(PersonEmailHistoryScope.Lead, leadId, emailId), cancellationToken));
}
