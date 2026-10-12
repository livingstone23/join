using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Commands.UpdateMyCalendarConfiguration;
using JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Commands.UpdateUserCalendarConfiguration;
using JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Queries.GetCalendarUserConfigurations;
using JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Queries.GetMyCalendarConfiguration;
using JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Queries.GetUserCalendarConfiguration;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Calendars;

/// <summary>
/// Calendar configuration of the users (SPEC 44): <c>/me</c> with resource <c>Calendar</c> (preferences only), the
/// rest with <c>CalendarSettings</c> (contract hours and work days).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("CalendarSettings")]
public class CalendarUserConfigurationsController(ISender sender) : ControllerBase
{
    /// <summary> My configuration with the effective hours. </summary>
    [HttpGet("me")]
    [PermissionResource("Calendar")]
    [ProducesResponseType(typeof(Response<CalendarUserConfigurationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetMyCalendarConfigurationQuery(), cancellationToken));

    /// <summary> Updates my preferences (default view, first day of the week). </summary>
    [HttpPut("me")]
    [PermissionResource("Calendar")]
    [ProducesResponseType(typeof(Response<CalendarUserConfigurationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMine([FromBody] UpdateMyCalendarConfigurationCommand command, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(command, cancellationToken));

    /// <summary> Users of the company with their configuration (also the ones without calendar). </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<CalendarUserConfigurationDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] string? search = null, [FromQuery] int? page = null, [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarUserConfigurationsQuery(search, page ?? pageNumber, pageSize), cancellationToken));

    /// <summary> Configuration of one user. </summary>
    [HttpGet("{userId:guid}")]
    [ProducesResponseType(typeof(Response<CalendarUserConfigurationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByUser(Guid userId, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetUserCalendarConfigurationQuery(userId), cancellationToken));

    /// <summary> Updates the configuration of one user. </summary>
    [HttpPut("{userId:guid}")]
    [ProducesResponseType(typeof(Response<CalendarUserConfigurationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateForUser(Guid userId, [FromBody] UpdateUserCalendarConfigurationCommand command, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(command with { UserId = userId }, cancellationToken));
}
