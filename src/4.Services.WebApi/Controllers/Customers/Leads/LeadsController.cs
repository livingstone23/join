using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.DTO.Customers.Leads;
using JOIN.Application.UseCases.Catalog.Products.Queries;
using JOIN.Application.UseCases.Customers.Leads.Commands;
using JOIN.Application.UseCases.Customers.Leads.Common;
using JOIN.Application.UseCases.Customers.Leads.Queries;
using JOIN.Domain.Security;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Customers.Leads;

/// <summary>
/// Leads (SPEC 61): inbox, board, detail, timeline, stage moves, contacts, comments, offered products, reassign,
/// inactivate and reactivate. What each user sees is limited by the lead scope of SPEC 60; inactive leads are visible
/// to the whole company.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("Leads")]
public class LeadsController(IMediator mediator) : ControllerBase
{
    /// <summary>The visible leads, paged, newest contact first. <c>status</c>: 1 Open (default), 2 Inactive, 3 Won, 4 Lost, 0 all.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<LeadListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? status = null,
        [FromQuery] Guid? leadStageId = null,
        [FromQuery] Guid? ownerUserId = null,
        [FromQuery] Guid? salesTeamId = null,
        [FromQuery] Guid? leadSourceId = null,
        [FromQuery] Guid? relationshipTypeId = null,
        [FromQuery] string? search = null,
        [FromQuery] DateOnly? fromDate = null,
        [FromQuery] DateOnly? toDate = null,
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(
            new GetLeadsQuery(status, leadStageId, ownerUserId, salesTeamId, leadSourceId, relationshipTypeId, search, fromDate, toDate, pageNumber, pageSize),
            cancellationToken));

    /// <summary>Per open stage: count, value in base currency and the first 20 cards.</summary>
    [HttpGet("board")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<LeadBoardColumnDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBoard([FromQuery] Guid? salesTeamId = null, [FromQuery] Guid? ownerUserId = null, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetLeadBoardQuery(salesTeamId, ownerUserId), cancellationToken));

    /// <summary>Open leads of a person, to warn before creating another one.</summary>
    [HttpGet("duplicates")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<LeadDuplicateDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDuplicates([FromQuery] Guid personId, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetLeadDuplicatesQuery(personId), cancellationToken));

    /// <summary>Active stages, sources, reasons, relationship types, contact types and currencies of the company.</summary>
    [HttpGet("catalogs")]
    [ProducesResponseType(typeof(Response<LeadCatalogsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCatalogs(CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetLeadCatalogsQuery(), cancellationToken));

    /// <summary>Active products of the company (SPEC 57) for the lead forms, under the <c>Leads</c> permission.</summary>
    [HttpGet("products-lookup")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<ProductLookupItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProductsLookup([FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetProductLookupQuery(search), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<LeadDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetLeadByIdQuery(id), cancellationToken));

    /// <summary>Contacts, comments, stage, owner and status changes, newest first.</summary>
    [HttpGet("{id:guid}/timeline")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<LeadTimelineItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTimeline(Guid id, [FromQuery] int? limit = null, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetLeadTimelineQuery(id, limit), cancellationToken));

    /// <summary>Creates a lead on an existing person or a new one. <c>duplicateOpenLeads</c> lists the person's open leads (policy <c>Warn</c>).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<LeadCreatedDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateLeadCommand command, CancellationToken cancellationToken = default) =>
        this.ToCommercialCreated(await mediator.Send(command, cancellationToken));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<LeadDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLeadCommand command, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(command with { Id = id }, cancellationToken));

    /// <summary>Moves the lead to the next active stage or back to an earlier one.</summary>
    [HttpPut("{id:guid}/stage")]
    [ProducesResponseType(typeof(Response<LeadDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangeStage(Guid id, [FromBody] ChangeLeadStageBody body, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new ChangeLeadStageCommand(id, body.LeadStageId), cancellationToken));

    /// <summary>New owner, same team. Needs a management role that can reassign the lead's team.</summary>
    [HttpPost("{id:guid}/reassign")]
    [RequirePermission(PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<LeadDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Reassign(Guid id, [FromBody] ReassignLeadCommand command, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(command with { Id = id }, cancellationToken));

    [HttpPost("{id:guid}/inactivate")]
    [RequirePermission(PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<LeadDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Inactivate(Guid id, [FromBody] InactivateLeadCommand command, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(command with { Id = id }, cancellationToken));

    /// <summary>Any user with update permission on leads: the caller becomes the owner and the lead takes the caller's team.</summary>
    [HttpPost("{id:guid}/reactivate")]
    [RequirePermission(PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<LeadDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reactivate(Guid id, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ReactivateLeadCommand? command, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send((command ?? new ReactivateLeadCommand()) with { Id = id }, cancellationToken));

    [HttpGet("{id:guid}/interactions")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<LeadInteractionDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInteractions(Guid id, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetLeadInteractionsQuery(id), cancellationToken));

    [HttpPost("{id:guid}/interactions")]
    [RequirePermission(PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<LeadInteractionDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddInteraction(Guid id, [FromBody] LeadInteractionInput body, CancellationToken cancellationToken = default) =>
        this.ToCommercialCreated(await mediator.Send(new AddLeadInteractionCommand { LeadId = id, Interaction = body }, cancellationToken));

    /// <summary>Only its author, within 24 hours.</summary>
    [HttpPut("{id:guid}/interactions/{interactionId:guid}")]
    [ProducesResponseType(typeof(Response<LeadInteractionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateInteraction(Guid id, Guid interactionId, [FromBody] LeadInteractionInput body, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new UpdateLeadInteractionCommand { LeadId = id, InteractionId = interactionId, Interaction = body }, cancellationToken));

    [HttpPost("{id:guid}/comments")]
    [RequirePermission(PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<LeadCommentDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddComment(Guid id, [FromBody] AddLeadCommentCommand command, CancellationToken cancellationToken = default) =>
        this.ToCommercialCreated(await mediator.Send(command with { LeadId = id }, cancellationToken));

    /// <summary>Only its author.</summary>
    [HttpPut("{id:guid}/comments/{commentId:guid}")]
    [ProducesResponseType(typeof(Response<LeadCommentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateComment(Guid id, Guid commentId, [FromBody] UpdateLeadCommentCommand command, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(command with { LeadId = id, CommentId = commentId }, cancellationToken));

    /// <summary>Logical delete, only its author.</summary>
    [HttpDelete("{id:guid}/comments/{commentId:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteComment(Guid id, Guid commentId, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new DeleteLeadCommentCommand(id, commentId), cancellationToken));

    /// <summary>Adds an offered product; the price defaults to the reference price in the lead currency.</summary>
    [HttpPost("{id:guid}/products")]
    [RequirePermission(PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<LeadProductDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddProduct(Guid id, [FromBody] LeadProductInput body, CancellationToken cancellationToken = default) =>
        this.ToCommercialCreated(await mediator.Send(new AddLeadProductCommand { LeadId = id, Line = body }, cancellationToken));

    [HttpPut("{id:guid}/products/{lineId:guid}")]
    [ProducesResponseType(typeof(Response<LeadProductDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProduct(Guid id, Guid lineId, [FromBody] UpdateLeadProductCommand command, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(command with { LeadId = id, LineId = lineId }, cancellationToken));

    [HttpDelete("{id:guid}/products/{lineId:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteProduct(Guid id, Guid lineId, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new DeleteLeadProductCommand(id, lineId), cancellationToken));
}

/// <summary>Body of <c>PUT /Leads/{id}/stage</c>.</summary>
public sealed record ChangeLeadStageBody(Guid LeadStageId);
