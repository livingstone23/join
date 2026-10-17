using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadReports;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Customers.LeadReports;
using JOIN.Domain.Security;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Customers.LeadReports;

/// <summary>
/// Commercial reports (SPEC 64): computed on the fly from the leads, in base currency, limited by the user's lead scope
/// (SPEC 60). Filters: <c>from</c>, <c>to</c> (inclusive, UTC, max 366 days; default the current month), <c>salesTeamId</c>
/// and <c>ownerUserId</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("LeadReports")]
public class LeadReportsController(IMediator mediator, IPermissionService permissionService, ICurrentUserService currentUserService) : ControllerBase
{
    private const string Resource = "LeadReports";

    [HttpGet("summary")]
    [ProducesResponseType(typeof(Response<LeadReportSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Summary([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? salesTeamId, [FromQuery] Guid? ownerUserId, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetLeadReportSummaryQuery(from, to, salesTeamId, ownerUserId), cancellationToken));

    [HttpGet("funnel")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<LeadReportFunnelStageDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Funnel([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? salesTeamId, [FromQuery] Guid? ownerUserId, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetLeadReportFunnelQuery(from, to, salesTeamId, ownerUserId), cancellationToken));

    [HttpGet("by-team")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<LeadReportTeamDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ByTeam([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? salesTeamId, [FromQuery] Guid? ownerUserId, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetLeadReportByTeamQuery(from, to, salesTeamId, ownerUserId), cancellationToken));

    [HttpGet("ranking")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<LeadReportRankingDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Ranking([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? salesTeamId, [FromQuery] Guid? ownerUserId, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetLeadReportRankingQuery(from, to, salesTeamId, ownerUserId), cancellationToken));

    [HttpGet("by-product")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<LeadReportProductDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ByProduct([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? salesTeamId, [FromQuery] Guid? ownerUserId, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetLeadReportByProductQuery(from, to, salesTeamId, ownerUserId), cancellationToken));

    [HttpGet("by-source")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<LeadReportSourceDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> BySource([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? salesTeamId, [FromQuery] Guid? ownerUserId, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetLeadReportBySourceQuery(from, to, salesTeamId, ownerUserId), cancellationToken));

    [HttpGet("by-contact-type")]
    [ProducesResponseType(typeof(Response<LeadReportContactTypesDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ByContactType([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? salesTeamId, [FromQuery] Guid? ownerUserId, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetLeadReportByContactTypeQuery(from, to, salesTeamId, ownerUserId), cancellationToken));

    /// <summary>
    /// Won leads of the period, paged. <c>export=true</c> returns every row (max 10 000) and needs <c>CanExport</c> on
    /// <c>LeadReports</c> (403 otherwise).
    /// </summary>
    [HttpGet("won-leads")]
    [ProducesResponseType(typeof(Response<PagedResult<LeadReportWonLeadDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> WonLeads(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? salesTeamId,
        [FromQuery] Guid? ownerUserId,
        [FromQuery] bool export = false,
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        if (export && !User.IsInRole("SuperAdmin")
            && !await permissionService.HasPermissionAsync(currentUserService.UserId ?? string.Empty, currentUserService.CompanyId.ToString(), Resource, HttpMethods.Get, PermissionFlags.CanExport))
        {
            return Forbid();
        }

        return this.ToCommercialResult(await mediator.Send(new GetLeadReportWonLeadsQuery(from, to, salesTeamId, ownerUserId, export, pageNumber, pageSize), cancellationToken));
    }
}
