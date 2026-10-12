using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.DeleteCalendarActivityCompany;
using JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.EnableCalendarActivityCompany;
using JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.UpdateCalendarActivityCompany;
using JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Queries.GetCalendarActivityCompanies;
using JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Queries.GetCalendarActivityCompanyById;
using JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Queries.GetUsableCalendarActivityCompanies;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Calendars;

/// <summary>
/// Activity types enabled in the company (SPEC 44). Resource <c>CalendarSettings</c>; the <c>usable</c> lookup uses
/// <c>Calendar</c> because every calendar user needs it.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("CalendarSettings")]
public class CalendarActivityCompaniesController(ISender sender) : ControllerBase
{
    /// <summary> Paged types of the company with effective and global values. </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<CalendarActivityCompanyDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] string? search = null, [FromQuery] bool? isActive = null,
        [FromQuery] int? page = null, [FromQuery] int? pageNumber = null, [FromQuery] int? pageSize = null, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarActivityCompaniesQuery(search, isActive, page ?? pageNumber, pageSize), cancellationToken));

    /// <summary> Lookup for the activity form: usable types only. </summary>
    [HttpGet("usable")]
    [PermissionResource("Calendar")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<CalendarActivityCompanyDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsable(CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetUsableCalendarActivityCompaniesQuery(), cancellationToken));

    /// <summary> Detail. </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<CalendarActivityCompanyDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarActivityCompanyByIdQuery(id), cancellationToken));

    /// <summary> Enables a global type, copying its values. </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<CalendarActivityCompanyDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Enable([FromBody] EnableCalendarActivityCompanyCommand command, CancellationToken cancellationToken = default) =>
        this.ToCreated(await sender.Send(command, cancellationToken));

    /// <summary> Edits the effective values. </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<CalendarActivityCompanyDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCalendarActivityCompanyCommand command, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(command with { Id = id }, cancellationToken));

    /// <summary> Removes the type; with activities it is deactivated instead (<c>data = false</c>). </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new DeleteCalendarActivityCompanyCommand(id), cancellationToken));
}
