using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.SalesTeams;
using JOIN.Application.UseCases.Customers.SalesTeams.Members.Commands;
using JOIN.Application.UseCases.Customers.SalesTeams.Teams.Commands;
using JOIN.Application.UseCases.Customers.SalesTeams.Teams.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace JOIN.Services.WebApi.Controllers.Customers.SalesTeams;

/// <summary>
/// Sales teams of the customers module and their memberships (SPEC 60). A leave never deletes a membership: it is
/// closed with <c>ValidTo</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("SalesTeams")]
public class SalesTeamsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Response<IReadOnlyList<SalesTeamDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool? includeInactive = null,
        [FromQuery] bool? includeDeleted = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetSalesTeamsQuery(includeInactive, includeDeleted, companyId), cancellationToken));

    /// <summary>
    /// Current memberships of the token's user in the token's company (team, role, primary, scope). Under the
    /// <c>Leads</c> permission: the new-lead form needs it.
    /// </summary>
    [HttpGet("me")]
    [PermissionResource("Leads")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<SalesTeamMemberDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetMySalesTeamsQuery(), cancellationToken));

    /// <summary>The team with every membership, current and historical.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<SalesTeamDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromQuery] bool? includeDeleted = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetSalesTeamByIdQuery(id, includeDeleted, companyId), cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(Response<SalesTeamDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateSalesTeamCommand command, CancellationToken cancellationToken = default) =>
        this.ToCommercialCreated(await mediator.Send(command, cancellationToken));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<SalesTeamDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSalesTeamCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest(Response<SalesTeamDto>.Error("INVALID_SALES_TEAM_ID", ["Route id and payload id must match."]));
        }

        return this.ToCommercialResult(await mediator.Send(command with { Id = id }, cancellationToken));
    }

    /// <summary>Logical delete; 409 <c>SALES_TEAM_IN_USE</c> while it has current memberships (deactivate it instead).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new DeleteSalesTeamCommand(id), cancellationToken));

    /// <summary>Restores a logically deleted team (SPEC 41-post). Restricted to the SuperAdmin role.</summary>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid id, [FromQuery] Guid? companyId = null, CancellationToken cancellationToken = default) =>
        this.ToRestoreResult(await mediator.Send(new RestoreSalesTeamCommand(id, companyId), cancellationToken));

    /// <summary>Memberships of the team; <c>currentOnly=true</c> leaves out the closed ones.</summary>
    [HttpGet("{id:guid}/members")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<SalesTeamMemberDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMembers(
        Guid id,
        [FromQuery] bool? currentOnly = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetSalesTeamMembersQuery(id, currentOnly, companyId), cancellationToken));

    /// <summary>Adds a user of the company to the team with a management role.</summary>
    [HttpPost("{id:guid}/members")]
    [ProducesResponseType(typeof(Response<SalesTeamMemberDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddSalesTeamMemberCommand command, CancellationToken cancellationToken = default) =>
        this.ToCommercialCreated(await mediator.Send(command with { SalesTeamId = id }, cancellationToken));

    /// <summary>Changes the role or makes the membership the user's primary one.</summary>
    [HttpPut("{id:guid}/members/{memberId:guid}")]
    [ProducesResponseType(typeof(Response<SalesTeamMemberDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMember(Guid id, Guid memberId, [FromBody] UpdateSalesTeamMemberCommand command, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(command with { SalesTeamId = id, MemberId = memberId }, cancellationToken));

    /// <summary>Closes the membership on <c>validTo</c> (default today). There is no <c>DELETE</c>.</summary>
    [HttpPost("{id:guid}/members/{memberId:guid}/close")]
    [RequirePermission(JOIN.Domain.Security.PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<SalesTeamMemberDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CloseMember(Guid id, Guid memberId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CloseSalesTeamMemberCommand? command, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send((command ?? new CloseSalesTeamMemberCommand()) with { SalesTeamId = id, MemberId = memberId }, cancellationToken));
}
