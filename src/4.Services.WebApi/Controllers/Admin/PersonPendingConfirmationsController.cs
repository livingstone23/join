using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Commands.LinkCustomerToPersonPendingConfirmation;
using JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Commands.ResolvePersonPendingConfirmation;
using JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Queries.GetPersonPendingConfirmationById;
using JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Queries.GetPersonPendingConfirmations;
using JOIN.Domain.Enums;
using JOIN.Services.WebApi.Controllers.Calendars;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Admin;

/// <summary>
/// Inbox of the persons created by the channel agent (SPEC 45), menu option <c>PendingPersons</c>, resource
/// <c>PersonPendingConfirmations</c>. Resolving or linking closes the review activity.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("PersonPendingConfirmations")]
public class PersonPendingConfirmationsController(ISender sender) : ControllerBase
{
    /// <summary> Paged inbox; <c>status</c> defaults to Pending (1), most recent first. </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<PersonPendingConfirmationDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? status = null,
        [FromQuery] Guid? channelId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetPersonPendingConfirmationsQuery(status, channelId, from, to, search, page, pageSize), cancellationToken));

    /// <summary> Detail with the person's contacts and activities. </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<PersonPendingConfirmationDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetPersonPendingConfirmationByIdQuery(id), cancellationToken));

    /// <summary> Closes as <c>Confirmed</c> or <c>Discarded</c> (notes required). An update of the pending. </summary>
    [HttpPost("{id:guid}/resolve")]
    [RequirePermission(Domain.Security.PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<ResolvePersonPendingConfirmationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] ResolvePersonPendingConfirmationBody body, CancellationToken cancellationToken = default)
    {
        var status = Enum.TryParse<PersonPendingConfirmationStatus>(body.Status, ignoreCase: true, out var parsed) ? parsed : default;
        return this.ToResult(await sender.Send(new ResolvePersonPendingConfirmationCommand(id, status, body.Notes), cancellationToken));
    }

    /// <summary> Links to a customer of the same person and closes as <c>LinkedToCustomer</c>. An update of the pending. </summary>
    [HttpPost("{id:guid}/link-customer")]
    [RequirePermission(Domain.Security.PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<LinkCustomerToPersonPendingConfirmationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LinkCustomer(Guid id, [FromBody] LinkCustomerBody body, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new LinkCustomerToPersonPendingConfirmationCommand(id, body.CustomerId), cancellationToken));

    /// <summary> Body of <c>POST /{id}/resolve</c>: <c>status</c> is <c>Confirmed</c> or <c>Discarded</c>. </summary>
    public sealed record ResolvePersonPendingConfirmationBody(string? Status, string? Notes);

    /// <summary> Body of <c>POST /{id}/link-customer</c>. </summary>
    public sealed record LinkCustomerBody(Guid CustomerId);
}
