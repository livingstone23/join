using System;
using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Commands.LinkCustomerToPersonPendingConfirmation;
using JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Commands.ResolvePersonPendingConfirmation;
using JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Queries.GetPersonPendingConfirmationById;
using JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Queries.GetPersonPendingConfirmations;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Enums;
using JOIN.Services.WebApi.Controllers.Calendars;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Admin;

/// <summary>
/// Inbox of <c>PersonPendingConfirmation</c> rows (SPEC 45 F6). The controller is the
/// <c>PersonPendingConfirmations</c> resource: it lives under the <c>Admin</c> folder, but the
/// permission it asks for is the SPEC 43 "PendingPersons" option with controller name
/// <c>PersonPendingConfirmations</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("PersonPendingConfirmations")]
public class PersonPendingConfirmationsController(ISender sender) : ControllerBase
{
    /// <summary> Paged inbox. Default <c>Status = Pending</c>; the supervisor screen calls it without arguments. </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<PersonPendingConfirmationDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaged(
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromQuery] int? status = null,
        [FromQuery] Guid? channelId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var response = await sender.Send(
            new GetPersonPendingConfirmationsQuery(companyId, status, channelId, from, to, search, page, pageSize),
            cancellationToken);
        return this.ToResult(response);
    }

    /// <summary> Detail with the person's contacts and the activities that target them. </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<PersonPendingConfirmationDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var response = await sender.Send(new GetPersonPendingConfirmationByIdQuery(companyId, id), cancellationToken);
        return this.ToResult(response);
    }

    /// <summary> Resolve as <c>Confirmed</c> or <c>Discarded</c> (notes required on Discarded). </summary>
    [HttpPost("{id:guid}/resolve")]
    [ProducesResponseType(typeof(Response<ResolvePersonPendingConfirmationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Resolve(
        Guid id,
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromBody] ResolvePersonPendingConfirmationBody body,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<PersonPendingConfirmationStatus>(body.Status, ignoreCase: true, out var status))
        {
            return BadRequest(Response<ResolvePersonPendingConfirmationDto>.Error(
                "INVALID_RESOLUTION_STATUS",
                ["status must be one of: Confirmed, Discarded."]));
        }

        var response = await sender.Send(
            new ResolvePersonPendingConfirmationCommand(companyId, id, status, body.Notes),
            cancellationToken);
        return this.ToResult(response);
    }

    /// <summary> Link the pending to a customer of the same person; closes the pending. </summary>
    [HttpPost("{id:guid}/link-customer")]
    [ProducesResponseType(typeof(Response<LinkCustomerToPersonPendingConfirmationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LinkCustomer(
        Guid id,
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromBody] LinkCustomerBody body,
        CancellationToken cancellationToken = default)
    {
        var response = await sender.Send(
            new LinkCustomerToPersonPendingConfirmationCommand(companyId, id, body.CustomerId),
            cancellationToken);
        return this.ToResult(response);
    }

    /// <summary> Body of <c>POST /{id}/resolve</c>. </summary>
    public sealed record ResolvePersonPendingConfirmationBody(string Status, string? Notes);

    /// <summary> Body of <c>POST /{id}/link-customer</c>. </summary>
    public sealed record LinkCustomerBody(Guid CustomerId);
}
