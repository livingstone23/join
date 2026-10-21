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
/// SPEC 72 — the emails of a person, under the <c>Persons</c> resource. Sending needs <c>CanExecute</c> (sending is not
/// creating nor editing the person, SPEC 17); the history, the addresses and the attachment candidates need <c>CanRead</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/Persons/{personId:guid}")]
[Produces("application/json")]
[PermissionResource("Persons")]
public class PersonEmailsController(IMediator mediator) : ControllerBase
{
    /// <summary>The valid e-mail addresses of the person, primary first; empty when it has none.</summary>
    [HttpGet("email-addresses")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<PersonEmailAddressDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEmailAddresses(Guid personId, CancellationToken cancellationToken = default) =>
        this.ToPersonEmailResult(await mediator.Send(new GetPersonEmailAddressesQuery(personId), cancellationToken));

    /// <summary>Sends an email to the person; the lead is optional (an open lead of the person in the user's scope).</summary>
    [HttpPost("emails")]
    [RequirePermission(PermissionFlags.CanExecute)]
    [ProducesResponseType(typeof(Response<PersonEmailSentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(Response<PersonEmailSentDto>), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Send(Guid personId, [FromBody] SendPersonEmailCommand command, CancellationToken cancellationToken = default) =>
        this.ToPersonEmailCreated(await mediator.Send(command with { PersonId = personId }, cancellationToken));

    /// <summary>The email history of the person, newest first.</summary>
    [HttpGet("emails")]
    [ProducesResponseType(typeof(Response<PagedResult<PersonEmailMessageListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid personId, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        this.ToPersonEmailResult(await mediator.Send(new GetPersonEmailsQuery(PersonEmailHistoryScope.Person, personId, pageNumber, pageSize), cancellationToken));

    /// <summary>Documents that can be attached: product documents (search, max. 50) and, with <c>leadId</c>, the lead documents.</summary>
    [HttpGet("emails/attachment-candidates")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<PersonEmailAttachmentCandidateDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AttachmentCandidates(Guid personId, [FromQuery] Guid? leadId = null, [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        this.ToPersonEmailResult(await mediator.Send(new GetPersonEmailAttachmentCandidatesQuery(personId, leadId, search), cancellationToken));

    /// <summary>The detail of an email of the person: body, copies and attachments.</summary>
    [HttpGet("emails/{emailId:guid}")]
    [ProducesResponseType(typeof(Response<PersonEmailMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detail(Guid personId, Guid emailId, CancellationToken cancellationToken = default) =>
        this.ToPersonEmailResult(await mediator.Send(new GetPersonEmailQuery(PersonEmailHistoryScope.Person, personId, emailId), cancellationToken));
}
