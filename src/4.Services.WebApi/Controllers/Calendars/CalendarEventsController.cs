using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.ChangeCalendarEventStatus;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.CreateCalendarEvent;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.DeleteCalendarEvent;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.UpdateCalendarEvent;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Queries.GetCalendarEventById;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Queries.GetCalendarEventLogs;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Queries.GetCalendarEventsRange;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Queries.GetUpcomingCalendarEvents;
using JOIN.Domain.Calendars.Enums;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Calendars;

/// <summary>
/// Activities of the current user (SPEC 44). Resource <c>Calendar</c>; the status change uses
/// <c>CalendarActivityConfirmation</c>. Another user's activity answers 404 (R2).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("Calendar")]
public class CalendarEventsController(ISender sender) : ControllerBase
{
    /// <summary> Expanded occurrences of my calendars in a range (at most 62 days), with holidays and my non-working periods. </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<CalendarEventRangeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRange([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        [FromQuery] Guid[]? calendarIds = null, [FromQuery] Guid[]? activityCompanyIds = null, [FromQuery] Guid[]? statusCompanyIds = null,
        [FromQuery] bool includeCancelled = false, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarEventsRangeQuery(from, to, calendarIds, activityCompanyIds, statusCompanyIds, includeCancelled), cancellationToken));

    /// <summary> My upcoming activities that are not cancelled nor completed. </summary>
    [HttpGet("upcoming")]
    [ProducesResponseType(typeof(Response<CalendarEventRangeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUpcoming([FromQuery] int days = 7, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetUpcomingCalendarEventsQuery(days), cancellationToken));

    /// <summary> Detail (for a series, the master with its rule). </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<CalendarEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarEventByIdQuery(id), cancellationToken));

    /// <summary> Log of the activity. </summary>
    [HttpGet("{id:guid}/logs")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<CalendarEventLogDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLogs(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarEventLogsQuery(id), cancellationToken));

    /// <summary> Creates an activity in my calendar. </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<CalendarEventDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCalendarEventCommand command, CancellationToken cancellationToken = default) =>
        this.ToCreated(await sender.Send(command, cancellationToken));

    /// <summary> Updates an activity; for a series <c>scope</c> and <c>occurrenceStartUtc</c> are required. </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<CalendarEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCalendarEventCommand command,
        [FromQuery] CalendarRecurrenceEditScope? scope = null, [FromQuery] DateTime? occurrenceStartUtc = null, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(command with { Id = id, Scope = scope, OccurrenceStartUtc = occurrenceStartUtc }, cancellationToken));

    /// <summary> Changes the status (confirm, cancel with notes, complete…). </summary>
    [HttpPatch("{id:guid}/status")]
    [PermissionResource("CalendarActivityConfirmation")]
    [ProducesResponseType(typeof(Response<CalendarEventDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeCalendarEventStatusCommand command,
        [FromQuery] CalendarRecurrenceEditScope? scope = null, [FromQuery] DateTime? occurrenceStartUtc = null, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(command with { Id = id, Scope = scope, OccurrenceStartUtc = occurrenceStartUtc }, cancellationToken));

    /// <summary> Logical delete (records created by mistake). </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] CalendarRecurrenceEditScope? scope = null,
        [FromQuery] DateTime? occurrenceStartUtc = null, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new DeleteCalendarEventCommand(id, scope, occurrenceStartUtc), cancellationToken));
}
