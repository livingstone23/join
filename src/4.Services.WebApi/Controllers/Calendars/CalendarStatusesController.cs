using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.CreateCalendarStatus;
using JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.DeleteCalendarStatus;
using JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.UpdateCalendarStatus;
using JOIN.Application.UseCases.Calendars.CalendarStatuses.Queries.GetCalendarStatusById;
using JOIN.Application.UseCases.Calendars.CalendarStatuses.Queries.GetCalendarStatuses;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Calendars;

/// <summary>
/// Global catalog of statuses (SPEC 44). Resource <c>CalendarCatalogs</c>. The five system statuses cannot be
/// deleted or deactivated.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("CalendarCatalogs")]
public class CalendarStatusesController(ISender sender) : ControllerBase
{
    /// <summary> Paged catalog. </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<CalendarStatusDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] string? search = null, [FromQuery] bool? isActive = null,
        [FromQuery] int? page = null, [FromQuery] int? pageNumber = null, [FromQuery] int? pageSize = null, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarStatusesQuery(search, isActive, page ?? pageNumber, pageSize), cancellationToken));

    /// <summary> Detail. </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<CalendarStatusDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarStatusByIdQuery(id), cancellationToken));

    /// <summary> Creates a status. </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<CalendarStatusDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateCalendarStatusCommand command, CancellationToken cancellationToken = default) =>
        this.ToCreated(await sender.Send(command, cancellationToken));

    /// <summary> Updates a status. </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<CalendarStatusDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCalendarStatusCommand command, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(command with { Id = id }, cancellationToken));

    /// <summary> Logical delete. </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new DeleteCalendarStatusCommand(id), cancellationToken));
}
