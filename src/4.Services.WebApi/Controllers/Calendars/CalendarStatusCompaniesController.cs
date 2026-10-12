using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.DeleteCalendarStatusCompany;
using JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.EnableCalendarStatusCompany;
using JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.UpdateCalendarStatusCompany;
using JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Queries.GetCalendarStatusCompanies;
using JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Queries.GetCalendarStatusCompanyById;
using JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Queries.GetUsableCalendarStatusCompanies;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Calendars;

/// <summary>
/// Statuses enabled in the company (SPEC 44). Resource <c>CalendarSettings</c>; <c>usable</c> uses <c>Calendar</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("CalendarSettings")]
public class CalendarStatusCompaniesController(ISender sender) : ControllerBase
{
    /// <summary> Paged statuses of the company. </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<CalendarStatusCompanyDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] string? search = null, [FromQuery] bool? isActive = null,
        [FromQuery] int? page = null, [FromQuery] int? pageNumber = null, [FromQuery] int? pageSize = null, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarStatusCompaniesQuery(search, isActive, page ?? pageNumber, pageSize), cancellationToken));

    /// <summary> Lookup of usable statuses. </summary>
    [HttpGet("usable")]
    [PermissionResource("Calendar")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<CalendarStatusCompanyDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsable(CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetUsableCalendarStatusCompaniesQuery(), cancellationToken));

    /// <summary> Detail. </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<CalendarStatusCompanyDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarStatusCompanyByIdQuery(id), cancellationToken));

    /// <summary> Enables a global status. </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<CalendarStatusCompanyDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Enable([FromBody] EnableCalendarStatusCompanyCommand command, CancellationToken cancellationToken = default) =>
        this.ToCreated(await sender.Send(command, cancellationToken));

    /// <summary> Edits name, color, active flag and order. </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<CalendarStatusCompanyDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCalendarStatusCompanyCommand command, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(command with { Id = id }, cancellationToken));

    /// <summary> Removes the status from the company. </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new DeleteCalendarStatusCompanyCommand(id), cancellationToken));
}
