using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.CalendarActivityIndex;
using JOIN.Application.UseCases.Calendars.CalendarActivityIndex.Queries;
using JOIN.Domain.Security;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Calendars;

/// <summary>
/// SPEC 68 — paginated and exportable list of activities with filters. Resource <c>CalendarActivityIndex</c>;
/// the default visibility is "own activities only", unless the user has the per-user
/// <c>CanViewOthersActivities</c> flag enabled in <c>CalendarUserConfiguration</c>. Export requires
/// <c>CanExport</c>; the controller checks it inline so <c>export=true</c> answers 403 when the user
/// lacks the flag, instead of returning the read-only page.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("CalendarActivityIndex")]
public class CalendarActivityIndexController(
    ISender sender,
    IPermissionService permissionService,
    ICurrentUserService currentUserService) : ControllerBase
{
    /// <summary>
    /// Returns the paginated list of activities in the range. Range capped at 92 days (rule 6).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<CalendarActivityIndexItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIndex([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        [FromQuery] Guid[]? activityCompanyIds = null, [FromQuery] Guid[]? statusCompanyIds = null,
        [FromQuery] Guid[]? ownerUserIds = null, [FromQuery] Guid[]? calendarIds = null,
        [FromQuery] Guid? requesterPersonId = null, [FromQuery] Guid[]? originChannelIds = null,
        [FromQuery] string? search = null, [FromQuery] bool includeCancelled = false,
        [FromQuery] int? pageNumber = null, [FromQuery] int? pageSize = null,
        [FromQuery] bool export = false, CancellationToken cancellationToken = default)
    {
        if (export)
        {
            var allowed = await permissionService.HasPermissionAsync(
                currentUserService.UserId,
                currentUserService.CompanyId.ToString(),
                "CalendarActivityIndex",
                "GET",
                PermissionFlags.CanExport);
            if (!allowed)
            {
                return new ForbidResult();
            }
        }

        var query = new CalendarActivityIndexQuery
        {
            FromUtc = from.UtcDateTime,
            ToUtc = to.UtcDateTime,
            ActivityCompanyIds = activityCompanyIds is { Length: > 0 } ? activityCompanyIds : null,
            StatusCompanyIds = statusCompanyIds is { Length: > 0 } ? statusCompanyIds : null,
            OwnerUserIds = ownerUserIds is { Length: > 0 } ? ownerUserIds : null,
            CalendarIds = calendarIds is { Length: > 0 } ? calendarIds : null,
            RequesterPersonId = requesterPersonId,
            OriginChannelIds = originChannelIds is { Length: > 0 } ? originChannelIds : null,
            Search = search,
            IncludeCancelled = includeCancelled,
            PageNumber = pageNumber ?? 1,
            PageSize = pageSize ?? 50,
            Export = export
        };
        return this.ToResult(await sender.Send(query, cancellationToken));
    }

    /// <summary>
    /// Returns the owners the user is allowed to pick from the filter. Without the per-user flag, this is the
    /// current user only.
    /// </summary>
    [HttpGet("owners")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<CalendarActivityIndexOwnerItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOwners([FromQuery] string? search = null, [FromQuery] int? limit = null, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarActivityIndexOwnersQuery(search, limit ?? 50), cancellationToken));

    /// <summary> Detail of one activity; 404 when the activity is not the user's and the flag is off. </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<CalendarEventDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarActivityIndexEventByIdQuery(id), cancellationToken));

    /// <summary> Log of one activity; same visibility rule as <see cref="GetById"/>. </summary>
    [HttpGet("{id:guid}/logs")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<CalendarEventLogDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLogs(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarActivityIndexEventLogsQuery(id), cancellationToken));
}