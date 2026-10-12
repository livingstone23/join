using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.ChangeCalendarPanelEventStatus;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.CreateCalendarPanelEvent;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.DeleteCalendarPanelEvent;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.UpdateCalendarPanelEvent;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelAvailability;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelCalendars;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelEventById;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelEventLogs;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelEvents;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelUsers;
using JOIN.Domain.Calendars.Enums;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Calendars;

/// <summary>
/// Calendars of other users of the same company (SPEC 44). Resource <c>CalendarOthers</c>; the status change uses
/// <c>CalendarActivityConfirmation</c> and the handler also requires <c>CalendarOthers.CanUpdate</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("CalendarOthers")]
public class CalendarPanelController(ISender sender) : ControllerBase
{
    /// <summary> Users of the company for the panel filter. </summary>
    [HttpGet("users")]
    [ProducesResponseType(typeof(Response<PagedResult<CalendarPanelUserDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers([FromQuery] string? search = null, [FromQuery] bool? onlyWithCalendar = null,
        [FromQuery] int? page = null, [FromQuery] int? pageNumber = null, [FromQuery] int? pageSize = null, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarPanelUsersQuery(search, onlyWithCalendar, page ?? pageNumber, pageSize), cancellationToken));

    /// <summary> Calendars of the selected users (at most 20). </summary>
    [HttpGet("calendars")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<UserCalendarDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCalendars([FromQuery] Guid[] userIds, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarPanelCalendarsQuery(userIds), cancellationToken));

    /// <summary> Occurrences of several users (at most 20 users and 62 days). </summary>
    [HttpGet("events")]
    [ProducesResponseType(typeof(Response<CalendarEventRangeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEvents([FromQuery] Guid[] userIds, [FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        [FromQuery] Guid[]? calendarIds = null, [FromQuery] Guid[]? activityCompanyIds = null, [FromQuery] Guid[]? statusCompanyIds = null,
        [FromQuery] bool includeCancelled = false, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarPanelEventsQuery(userIds, from, to, calendarIds, activityCompanyIds, statusCompanyIds, includeCancelled), cancellationToken));

    /// <summary> Detail of another user's activity. </summary>
    [HttpGet("events/{id:guid}")]
    [ProducesResponseType(typeof(Response<CalendarEventDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEventById(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarPanelEventByIdQuery(id), cancellationToken));

    /// <summary> Log of another user's activity. </summary>
    [HttpGet("events/{id:guid}/logs")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<CalendarEventLogDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEventLogs(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarPanelEventLogsQuery(id), cancellationToken));

    /// <summary> Free slots of a user for an activity type (at most 14 days). </summary>
    [HttpGet("availability")]
    [ProducesResponseType(typeof(Response<CalendarAvailabilityDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailability([FromQuery] Guid userId, [FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        [FromQuery] Guid activityCompanyId, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarPanelAvailabilityQuery(userId, from, to, activityCompanyId), cancellationToken));

    /// <summary> Creates an activity in another user's calendar (R5 applies). </summary>
    [HttpPost("events")]
    [ProducesResponseType(typeof(Response<CalendarEventDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateEvent([FromBody] CreateCalendarPanelEventCommand command, CancellationToken cancellationToken = default) =>
        this.ToCreated(await sender.Send(command, cancellationToken));

    /// <summary> Updates another user's activity (R5; reschedule status when the time changes). </summary>
    [HttpPut("events/{id:guid}")]
    [ProducesResponseType(typeof(Response<CalendarEventDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateEvent(Guid id, [FromBody] UpdateCalendarPanelEventCommand command,
        [FromQuery] CalendarRecurrenceEditScope? scope = null, [FromQuery] DateTime? occurrenceStartUtc = null, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(command with { Id = id, Scope = scope, OccurrenceStartUtc = occurrenceStartUtc }, cancellationToken));

    /// <summary> Changes the status of another user's activity. </summary>
    [HttpPatch("events/{id:guid}/status")]
    [PermissionResource("CalendarActivityConfirmation")]
    [ProducesResponseType(typeof(Response<CalendarEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ChangeEventStatus(Guid id, [FromBody] ChangeCalendarPanelEventStatusCommand command,
        [FromQuery] CalendarRecurrenceEditScope? scope = null, [FromQuery] DateTime? occurrenceStartUtc = null, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(command with { Id = id, Scope = scope, OccurrenceStartUtc = occurrenceStartUtc }, cancellationToken));

    /// <summary> Logical delete of another user's activity. </summary>
    [HttpDelete("events/{id:guid}")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteEvent(Guid id, [FromQuery] CalendarRecurrenceEditScope? scope = null,
        [FromQuery] DateTime? occurrenceStartUtc = null, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new DeleteCalendarPanelEventCommand(id, scope, occurrenceStartUtc), cancellationToken));
}
