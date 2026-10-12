using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.CreateMyNonWorkingPeriod;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.CreateUserNonWorkingPeriod;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.DeleteMyNonWorkingPeriod;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.DeleteUserNonWorkingPeriod;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.UpdateUserNonWorkingPeriod;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Queries.GetMyNonWorkingPeriods;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Queries.GetUserNonWorkingPeriods;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Calendars;

/// <summary>
/// Non-working periods (SPEC 44): <c>/me</c> with resource <c>Calendar</c>, other users with <c>CalendarSettings</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("CalendarSettings")]
public class CalendarUserNonWorkingPeriodsController(ISender sender) : ControllerBase
{
    /// <summary> My periods of a year. </summary>
    [HttpGet("me")]
    [PermissionResource("Calendar")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<CalendarUserNonWorkingPeriodDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine([FromQuery] int? year = null, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetMyNonWorkingPeriodsQuery(year), cancellationToken));

    /// <summary> Registers one of my periods; returns the affected activities. </summary>
    [HttpPost("me")]
    [PermissionResource("Calendar")]
    [ProducesResponseType(typeof(Response<CalendarNonWorkingPeriodResultDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateMine([FromBody] CreateMyNonWorkingPeriodCommand command, CancellationToken cancellationToken = default) =>
        this.ToCreated(await sender.Send(command, cancellationToken));

    /// <summary> Deletes one of my periods. </summary>
    [HttpDelete("me/{id:guid}")]
    [PermissionResource("Calendar")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteMine(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new DeleteMyNonWorkingPeriodCommand(id), cancellationToken));

    /// <summary> Paged periods of the company, optionally of one user and year. </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<CalendarUserNonWorkingPeriodDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] Guid? userId = null, [FromQuery] int? year = null, [FromQuery] int? page = null,
        [FromQuery] int? pageNumber = null, [FromQuery] int? pageSize = null, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetUserNonWorkingPeriodsQuery(userId, year, page ?? pageNumber, pageSize), cancellationToken));

    /// <summary> Registers a period of another user (<c>userId</c> in the body). </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<CalendarNonWorkingPeriodResultDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateUserNonWorkingPeriodCommand command, CancellationToken cancellationToken = default) =>
        this.ToCreated(await sender.Send(command, cancellationToken));

    /// <summary> Updates a period. </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<CalendarNonWorkingPeriodResultDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserNonWorkingPeriodCommand command, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(command with { Id = id }, cancellationToken));

    /// <summary> Deletes a period. </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new DeleteUserNonWorkingPeriodCommand(id), cancellationToken));
}
