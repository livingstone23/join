using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.CreateCalendarActivity;
using JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.DeleteCalendarActivity;
using JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.UpdateCalendarActivity;
using JOIN.Application.UseCases.Calendars.CalendarActivities.Queries.GetCalendarActivities;
using JOIN.Application.UseCases.Calendars.CalendarActivities.Queries.GetCalendarActivityById;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Calendars;

/// <summary>
/// Global catalog of activity types (SPEC 44). Resource <c>CalendarCatalogs</c>: by default only SuperAdmin.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("CalendarCatalogs")]
public class CalendarActivitiesController(ISender sender) : ControllerBase
{
    /// <summary> Paged catalog. </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<CalendarActivityDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] string? search = null, [FromQuery] bool? isActive = null,
        [FromQuery] int? page = null, [FromQuery] int? pageNumber = null, [FromQuery] int? pageSize = null, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarActivitiesQuery(search, isActive, page ?? pageNumber, pageSize), cancellationToken));

    /// <summary> Detail. </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<CalendarActivityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarActivityByIdQuery(id), cancellationToken));

    /// <summary> Creates a type (<c>CALENDAR_ACTIVITY_CODE_DUPLICATE</c>). </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<CalendarActivityDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCalendarActivityCommand command, CancellationToken cancellationToken = default) =>
        this.ToCreated(await sender.Send(command, cancellationToken));

    /// <summary> Updates a type; companies keep their own values. </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<CalendarActivityDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCalendarActivityCommand command, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(command with { Id = id }, cancellationToken));

    /// <summary> Logical delete; blocked when enabled in a company or a system code. </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new DeleteCalendarActivityCommand(id), cancellationToken));
}
