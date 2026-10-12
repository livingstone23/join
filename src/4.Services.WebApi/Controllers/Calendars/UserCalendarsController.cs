using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.UserCalendars.Commands.CreateUserCalendar;
using JOIN.Application.UseCases.Calendars.UserCalendars.Commands.DeleteUserCalendar;
using JOIN.Application.UseCases.Calendars.UserCalendars.Commands.SetDefaultUserCalendar;
using JOIN.Application.UseCases.Calendars.UserCalendars.Commands.UpdateUserCalendar;
using JOIN.Application.UseCases.Calendars.UserCalendars.Queries.GetMyCalendars;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Calendars;

/// <summary>
/// Calendars of the current user (SPEC 44). Resource <c>Calendar</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("Calendar")]
public class UserCalendarsController(ISender sender) : ControllerBase
{
    /// <summary> My calendars in the current company (<c>pendingProvisioning</c> when I have none). </summary>
    [HttpGet("mine")]
    [ProducesResponseType(typeof(Response<MyCalendarsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetMyCalendarsQuery(), cancellationToken));

    /// <summary> Creates a calendar. </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<UserCalendarDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateUserCalendarCommand command, CancellationToken cancellationToken = default) =>
        this.ToCreated(await sender.Send(command, cancellationToken));

    /// <summary> Updates a calendar. </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<UserCalendarDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserCalendarCommand command, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(command with { Id = id }, cancellationToken));

    /// <summary> Marks the calendar as my default. </summary>
    [HttpPut("{id:guid}/set-default")]
    [ProducesResponseType(typeof(Response<UserCalendarDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetDefault(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new SetDefaultUserCalendarCommand(id), cancellationToken));

    /// <summary> Deletes a calendar (not the default one, not with upcoming activities). </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new DeleteUserCalendarCommand(id), cancellationToken));
}
